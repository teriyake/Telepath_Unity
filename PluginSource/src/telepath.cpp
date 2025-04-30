#include <atomic>
#include <chrono>
#include <condition_variable>
#include <cstdint>
#include <cstdio>
#include <iostream>
#include <mutex>
#include <queue>
#include <stdarg.h>
#include <string>
#include <thread>
#include <vector>

#ifdef _WIN32
#define WIN32_LEAN_AND_MEAN
#include <windows.h>
#include <winsock2.h>
#include <ws2tcpip.h>
#pragma comment(lib, "Ws2_32.lib")
typedef SOCKET socket_t;
#define IS_SOCKET_INVALID(s) (s == INVALID_SOCKET)
#define close_socket(s) closesocket(s)
#define get_last_socket_error() WSAGetLastError()
#define EWOULDBLOCK WSAEWOULDBLOCK
#define EINTR WSAEINTR
#else // POSIX
#include <arpa/inet.h>
#include <errno.h>
#include <netdb.h>
#include <poll.h>
#include <sys/socket.h>
#include <sys/types.h>
#include <unistd.h>
typedef int socket_t;
#define IS_SOCKET_INVALID(s) (s < 0)
#define close_socket(s) close(s)
#define get_last_socket_error() (errno)
#endif


#include "ip/UdpSocket.h"
#include "osc/OscOutboundPacketStream.h"
#include "osc/OscPacketListener.h"
#include "osc/OscReceivedElements.h"

#define VCV_RACK_IP "127.0.0.1"
#define VCV_RACK_PORT 7001
#define OUTPUT_BUFFER_SIZE 4096

typedef void (*DebugLogFuncPtr)(const char* message);

enum LogLevel
{
	LOG_DEBUG,
	LOG_INFO,
	LOG_WARNING,
	LOG_ERROR
};

static LogLevel g_currentLogLevel = LOG_INFO;

static DebugLogFuncPtr g_debugLogCallback = nullptr;

void LogToUnity(LogLevel level, const char* format, ...)
{
	if (level >= g_currentLogLevel && g_debugLogCallback)
	{
		char buffer[4096];
		va_list args;
		va_start(args, format);
		vsnprintf(buffer, sizeof(buffer), format, args);
		va_end(args);

		buffer[sizeof(buffer) - 1] = '\0';

		g_debugLogCallback(buffer);
	}
}

namespace
{
UdpTransmitSocket* g_transmitSocket = nullptr;
std::mutex g_socketMutex;
bool g_isInitialized = false;
bool g_errorLoggedSocketNotInit = false;
} // namespace

namespace
{
struct ReceivedOscMessage
{
	std::string address;
	float value;
};

UdpListeningReceiveSocket* g_receiveSocket = nullptr;
std::thread g_listenerThread;
std::atomic<bool> g_listenerRunning{false};
std::mutex g_messageQueueMutex;
std::queue<ReceivedOscMessage> g_messageQueue;
std::condition_variable g_queueCondition;

int g_listenPort = 0;
bool g_errorLoggedListenerNotInit = false;


class UnityOscListener : public osc::OscPacketListener
{
protected:
	virtual void ProcessMessage(const osc::ReceivedMessage& m,
								const IpEndpointName& remoteEndpoint) override
	{
		try
		{
			if (m.ArgumentCount() == 1 && m.ArgumentsBegin()->IsFloat())
			{
				ReceivedOscMessage msg;
				msg.address = m.AddressPattern();
				msg.value = m.ArgumentsBegin()->AsFloat();

				{
					std::lock_guard<std::mutex> lock(g_messageQueueMutex);
					const size_t MAX_QUEUE_SIZE = 1024;
					if (g_messageQueue.size() < MAX_QUEUE_SIZE)
					{
						g_messageQueue.push(msg);
					}
					else
					{
						LogToUnity(
							LOG_ERROR,
							"[NativePlugin] OSC message queue overflow!");
						return;
					}
				}
				g_queueCondition.notify_one();
			}
			else
			{
				LogToUnity(LOG_WARNING,
						   "[NativePlugin] Received OSC message with unhandled "
						   "arguments.");
			}
		}
		catch (const osc::Exception& e)
		{
			LogToUnity(
				LOG_ERROR,
				"[NativePlugin] Error parsing OSC message: %s (Address: %s)",
				e.what(), m.AddressPattern());
		}
		catch (const std::exception& e)
		{
			LogToUnity(LOG_ERROR,
					   "[NativePlugin] Standard exception processing OSC "
					   "message: %s (Address: %s)",
					   e.what(), m.AddressPattern());
		}
		catch (...)
		{
			LogToUnity(LOG_ERROR,
					   "[NativePlugin] Unknown error processing OSC message "
					   "(Address: %s)",
					   m.AddressPattern());
		}
	}
};

UnityOscListener g_oscListener;

void ListenerThreadFunction(int port)
{
	try
	{
		g_receiveSocket = new UdpListeningReceiveSocket(
			IpEndpointName(IpEndpointName::ANY_ADDRESS, port), &g_oscListener);
		LogToUnity(LOG_INFO, "[NativePlugin] OSC Listener started on port %d",
				   port);
		g_listenerRunning = true;
		g_errorLoggedListenerNotInit = false;
		g_receiveSocket->Run();
	}
	catch (const std::exception& e)
	{
		LogToUnity(LOG_ERROR,
				   "[NativePlugin] OSC Listener thread exception: %s",
				   e.what());
		delete g_receiveSocket;
		g_receiveSocket = nullptr;
	}
	catch (...)
	{
		LogToUnity(LOG_ERROR,
				   "[NativePlugin] OSC Listener thread unknown exception.");
		delete g_receiveSocket;
		g_receiveSocket = nullptr;
	}
	g_listenerRunning = false;
	LogToUnity(LOG_INFO, "[NativePlugin] OSC Listener stopped.");
}

} // namespace

namespace
{
UdpListeningReceiveSocket* g_audioReceiveSocket = nullptr;
std::thread g_audioListenerThread;
std::atomic<bool> g_audioListenerRunning{false};
std::mutex g_audioBufferMutex;
std::vector<float> g_audioCircularBuffer;
std::atomic<size_t> g_audioWriteIndex{0};
std::atomic<size_t> g_audioReadIndex{0};
size_t g_audioBufferSize = 0;

const int AUDIO_LISTEN_PORT = 7002;
const int AUDIO_SAMPLE_RATE = 48000;
const int AUDIO_CHANNELS = 2;
const size_t AUDIO_BUFFER_SIZE_SECONDS = 8;
} // namespace

namespace
{
#ifdef _WIN32
HANDLE g_audioShutdownEvent = NULL;
#else // POSIX
int audio_pipefd[2] = {-1, -1};
#endif
} // namespace

namespace
{

#ifdef _WIN32
// --- Windows-Specific Globals ---
std::atomic<int> g_winsockInitCount = 0;
std::mutex g_winsockMutex;

bool InitializeWinsock()
{
	std::lock_guard<std::mutex> lock(g_winsockMutex);
	if (g_winsockInitCount++ == 0)
	{
		WSADATA wsaData;
		int result = WSAStartup(MAKEWORD(2, 2), &wsaData);
		if (result != 0)
		{
			std::cerr << "[NativePlugin] WSAStartup failed: " << result
					  << std::endl;
			g_winsockInitCount = 0;
			return false;
		}
		LogToUnity(LOG_DEBUG, "[NativePlugin] Winsock initialized.");
	}
	return true;
}

void ShutdownWinsock()
{
	std::lock_guard<std::mutex> lock(g_winsockMutex);
	if (g_winsockInitCount == 0)
		return;

	if (--g_winsockInitCount == 0)
	{
		if (WSACleanup() == 0)
		{
			LogToUnity(LOG_DEBUG, "[NativePlugin] Winsock cleaned up.");
		}
		else
		{
			LogToUnity(LOG_ERROR, "[NativePlugin] WSACleanup failed: %d",
					   WSAGetLastError());
		}
	}
	// if (g_winsockInitCount < 0) {
	//    LogToUnity(LOG_WARNING, "[NativePlugin] Winsock init count went
	//    below zero!"); g_winsockInitCount = 0;
	// }
}
#else // POSIX
// define no-op versions for consistent calling code
bool InitializeWinsock()
{
	return true;
}
void ShutdownWinsock() {}
#endif

} // namespace


void AudioListenerThreadFunction(int port)
{
	LogToUnity(LOG_INFO,
			   "[NativePlugin] Audio Listener thread started on port %d", port);
	g_audioListenerRunning = true;

	int audioSocket = -1;

	try
	{
		audioSocket = socket(AF_INET, SOCK_DGRAM, IPPROTO_UDP);
		if (IS_SOCKET_INVALID(audioSocket))
		{
#ifdef _WIN32
			LogToUnity(LOG_ERROR,
					   "[NativePlugin] Failed to  create audio socket: %d",
					   get_last_socket_error());
#else
			LogToUnity(LOG_ERROR,
					   "[NativePlugin] Failed to create audio socket: %s (%d)",
					   strerror(get_last_socket_error()),
					   get_last_socket_error());
#endif
			g_audioListenerRunning = false;
			return;
		}

		sockaddr_in serverAddress{};
		serverAddress.sin_family = AF_INET;
		serverAddress.sin_addr.s_addr = htonl(INADDR_ANY);
		serverAddress.sin_port = htons(port);

		if (bind(audioSocket, (struct sockaddr*)&serverAddress,
				 sizeof(serverAddress)) == -1)
		{
#ifdef _WIN32
			LogToUnity(
				LOG_ERROR,
				"[NativePlugin] Failed to bind audio socket to port %d: %d",
				port, get_last_socket_error());
#else
			LogToUnity(LOG_ERROR,
					   "[NativePlugin] Failed to bind audio socket to port %d: "
					   "%s (%d)",
					   port, strerror(get_last_socket_error()),
					   get_last_socket_error());
#endif
			close_socket(audioSocket);
			g_audioListenerRunning = false;
			return;
		}

		LogToUnity(LOG_INFO,
				   "[NativePlugin] Audio Listener socket bound to port %d",
				   port);

		const size_t MAX_PACKET_SIZE = 65507;
		std::vector<char> buffer(MAX_PACKET_SIZE);

		while (g_audioListenerRunning)
		{
			int activity = -1;

#ifdef _WIN32
			HANDLE waitHandles[2] = {(HANDLE)audioSocket, g_audioShutdownEvent};
			fd_set readfds;
			FD_ZERO(&readfds);
			FD_SET(audioSocket, &readfds);
			struct timeval tv;
			tv.tv_sec = 0;
			tv.tv_usec = 100 * 1000;

			if (WaitForSingleObject(g_audioShutdownEvent, 0) == WAIT_OBJECT_0)
			{
				LogToUnity(LOG_INFO,
						   "[NativePlugin] Audio shutdown signal received.");
				g_audioListenerRunning = false;
				break;
			}

			activity = select(0, &readfds, NULL, NULL, &tv);

#else // POSIX
			struct pollfd fds[2];
			fds[0].fd = audioSocket;
			fds[0].events = POLLIN;
			fds[1].fd = audio_pipefd[0];
			fds[1].events = POLLIN;

			activity = poll(fds, 2, 100);
#endif
			if (activity == -1)
			{
#ifdef _WIN32
				int error_code = get_last_socket_error();
				if (error_code != WSAEINTR)
				{
					LogToUnity(LOG_ERROR,
							   "[NativePlugin] Audio listener select error: %d",
							   error_code);
					g_audioListenerRunning = false;
				}
#else // POSIX
				if (errno != EINTR)
				{
					LogToUnity(LOG_ERROR,
							   "[NativePlugin] Audio listener poll error: %s",
							   strerror(errno));
					g_audioListenerRunning = false;
				}
#endif
				continue;
			}

			if (activity == 0)
			{
				continue;
			}

			bool data_received = false;
#ifdef _WIN32
			if (activity > 0 && FD_ISSET(audioSocket, &readfds))
			{
				data_received = true;
			}
#else // POSIX
			if (activity > 0 && (fds[0].revents & POLLIN))
			{
				data_received = true;
			}
#endif

			if (data_received)
			{
				sockaddr_storage clientAddress{};
				socklen_t clientAddressLen = sizeof(clientAddress);
				size_t bytesReceived = recvfrom(
					audioSocket, buffer.data(), static_cast<int>(buffer.size()),
					0, (struct sockaddr*)&clientAddress, &clientAddressLen);

				if (bytesReceived == (size_t)-1)
				{
					int error_code = get_last_socket_error();
					if (error_code != EWOULDBLOCK && error_code != EAGAIN)
					{
						LogToUnity(LOG_ERROR,
								   "[NativePlugin] Audio recvfrom error: %d",
								   error_code);
						g_audioListenerRunning = false;
					}
				}
				if (bytesReceived > 0)
				{
					LogToUnity(
						LOG_DEBUG,
						"[NativePlugin] Received %zu bytes of audio data.",
						bytesReceived);
				}

				size_t numSamples = bytesReceived / sizeof(float);

				if (bytesReceived % sizeof(float) != 0)
				{
					LogToUnity(
						LOG_WARNING,
						"[NativePlugin] Warning: Received audio packet size "
						"(%zu bytes) is not a multiple of float size.",
						bytesReceived);
					continue;
				}

				const float* receivedSamples =
					reinterpret_cast<const float*>(buffer.data());

				{
					std::lock_guard<std::mutex> lock(g_audioBufferMutex);
					for (size_t i = 0; i < numSamples; ++i)
					{
						g_audioCircularBuffer[g_audioWriteIndex] =
							receivedSamples[i];
						g_audioWriteIndex =
							(g_audioWriteIndex + 1) % g_audioBufferSize;

						if (g_audioWriteIndex == g_audioReadIndex)
						{
							g_audioReadIndex =
								(g_audioReadIndex + 1) % g_audioBufferSize;
						}
					}
				}
			}

#ifndef _WIN32
			if (fds[1].revents & POLLIN)
			{
				char signal_byte;
				read(audio_pipefd[0], &signal_byte, 1);
				g_audioListenerRunning = false;
				LogToUnity(LOG_INFO,
						   "[NativePlugin] Audio shutdown signal received.");
			}
#endif
		}
	}
	catch (const std::exception& e)
	{
		LogToUnity(LOG_ERROR,
				   "[NativePlugin] Audio Listener thread exception: %s",
				   e.what());
	}
	catch (...)
	{
		LogToUnity(LOG_ERROR,
				   "[NativePlugin] Audio Listener thread unknown exception.");
	}

	if (!IS_SOCKET_INVALID(audioSocket))
	{
		close_socket(audioSocket);
	}

	g_audioListenerRunning = false;
	LogToUnity(LOG_INFO, "[NativePlugin] Audio Listener thread stopped.");
}

#ifdef __cplusplus
extern "C"
{
#endif

#ifdef _WIN32
#define DLL_EXPORT __declspec(dllexport)
#elif defined(__GNUC__) || defined(__clang__)
#define DLL_EXPORT __attribute__((visibility("default")))
#else
#define DLL_EXPORT
#warning "Compiler does not support DLL export directives"
#endif

	DLL_EXPORT bool InitializeTelepathAudioListener(int port)
	{
		if (!InitializeWinsock())
			return false;

		if (g_audioListenerRunning)
		{
			LogToUnity(LOG_INFO,
					   "[NativePlugin] InitializeTelepathAudioListener: "
					   "Already running.");
			return true;
		}

		if (port <= 0 || port >= 65536)
		{
			LogToUnity(LOG_ERROR,
					   "[NativePlugin] InitializeTelepathAudioListener: "
					   "Invalid port %d.",
					   port);

			ShutdownWinsock();
			return false;
		}

		LogToUnity(LOG_INFO,
				   "[NativePlugin] InitializeTelepathAudioListener: Attempting "
				   "to start on port %d...",
				   port);

		g_audioBufferSize =
			AUDIO_SAMPLE_RATE * AUDIO_CHANNELS * AUDIO_BUFFER_SIZE_SECONDS;
		g_audioCircularBuffer.resize(g_audioBufferSize);
		g_audioWriteIndex = 0;
		g_audioReadIndex = 0;
#ifdef _WIN32
		g_audioShutdownEvent = CreateEvent(NULL, TRUE, FALSE, NULL);
		if (g_audioShutdownEvent == NULL)
		{
			LogToUnity(
				LOG_ERROR,
				"[NativePlugin] Failed to create audio shutdown event: %lu",
				GetLastError());
			ShutdownWinsock();
			return false;
		}
		LogToUnity(LOG_INFO, "[NativePlugin] Audio shutdown event created.");
#else // POSIX
	if (pipe(audio_pipefd) == -1)
	{
		LogToUnity(LOG_ERROR,
				   "[NativePlugin] Failed to create audio shutdown pipe: %s",
				   strerror(errno));
		g_audioBufferSize = 0;
		g_audioCircularBuffer.clear();
		return false;
	}
	LogToUnity(LOG_INFO, "[NativePlugin] Audio shutdown pipe created.");
#endif

		try
		{
			g_audioListenerThread =
				std::thread(AudioListenerThreadFunction, port);
			// g_audioListenerThread.detach();
			return true;
		}
		catch (const std::system_error& e)
		{
			LogToUnity(
				LOG_ERROR,
				"[NativePlugin] Failed to create audio listener thread: %s",
				e.what());
			g_audioListenerRunning = false;
			g_audioBufferSize = 0;
			g_audioCircularBuffer.clear();
#ifdef _WIN32
			if (g_audioShutdownEvent != NULL)
				CloseHandle(g_audioShutdownEvent);
			g_audioShutdownEvent = NULL;
#else // POSIX
		close(audio_pipefd[0]);
		close(audio_pipefd[1]);
		audio_pipefd[0] = -1;
		audio_pipefd[1] = -1;
#endif
			ShutdownWinsock();
			return false;
		}
		catch (...)
		{
			LogToUnity(
				LOG_ERROR,
				"[NativePlugin] Unknown error creating audio listener thread.");
			g_audioListenerRunning = false;
			g_audioBufferSize = 0;
			g_audioCircularBuffer.clear();

#ifdef _WIN32
			if (g_audioShutdownEvent != NULL)
				CloseHandle(g_audioShutdownEvent);
			g_audioShutdownEvent = NULL;
#else // POSIX
		close(audio_pipefd[0]);
		close(audio_pipefd[1]);
		audio_pipefd[0] = -1;
		audio_pipefd[1] = -1;
#endif
			ShutdownWinsock();
			return false;
		}
	}

	DLL_EXPORT void ShutdownTelepathAudioListener()
	{
		if (!g_audioListenerRunning && !g_audioListenerThread.joinable())
		{
			LogToUnity(LOG_INFO,
					   "[NativePlugin] ShutdownTelepathAudioListener: Listener "
					   "not running or already shut down.");
			ShutdownWinsock();
			return;
		}

		LogToUnity(LOG_INFO,
				   "[NativePlugin] ShutdownTelepathAudioListener: Stopping "
				   "listener...");
		g_audioListenerRunning = false;

#ifdef _WIN32
		if (g_audioShutdownEvent != NULL)
		{
			SetEvent(g_audioShutdownEvent);
		}
#else // POSIX
	if (audio_pipefd[1] != -1)
	{
		char signal_byte = 1;
		if (write(audio_pipefd[1], &signal_byte, 1) == -1)
		{
			LogToUnity(
				LOG_ERROR,
				"[NativePlugin] Failed to write to audio shutdown pipe: %s",
				strerror(errno));
		}
	}
#endif

		if (g_audioListenerThread.joinable())
		{
			g_audioListenerThread.join();
		}

#ifdef _WIN32
		if (g_audioShutdownEvent != NULL)
		{
			CloseHandle(g_audioShutdownEvent);
			g_audioShutdownEvent = NULL;
		}
#else // POSIX
	if (audio_pipefd[0] != -1)
	{
		close(audio_pipefd[0]);
		audio_pipefd[0] = -1;
	}
	if (audio_pipefd[1] != -1)
	{
		close(audio_pipefd[1]);
		audio_pipefd[1] = -1;
	}
#endif

		g_audioCircularBuffer.clear();
		g_audioBufferSize = 0;
		g_audioWriteIndex = 0;
		g_audioReadIndex = 0;

		LogToUnity(LOG_INFO,
				   "[NativePlugin] Audio Listener shut down complete.");
		ShutdownWinsock();
	}

	DLL_EXPORT void RegisterDebugCallback(DebugLogFuncPtr callback)
	{
		g_debugLogCallback = callback;
		if (g_debugLogCallback)
		{
			LogToUnity(LOG_INFO, "[NativePlugin] Debug callback registered.");
		}
	}

	DLL_EXPORT int GetTelepathAudioSamples(float* buffer, int bufferSize)
	{
		if (!g_audioListenerRunning || buffer == nullptr || bufferSize <= 0)
		{
			return 0;
		}

		LogToUnity(
			LOG_DEBUG,
			"[NativePlugin] GetTelepathAudioSamples called,bufferSize: %d",
			bufferSize);

		std::lock_guard<std::mutex> lock(g_audioBufferMutex);

		size_t availableSamples;
		if (g_audioWriteIndex >= g_audioReadIndex)
		{
			availableSamples = g_audioWriteIndex - g_audioReadIndex;
		}
		else
		{
			availableSamples =
				g_audioBufferSize - g_audioReadIndex + g_audioWriteIndex;
		}

		size_t samplesToRead = std::min((size_t)bufferSize, availableSamples);

		if (samplesToRead == 0)
		{
			return 0;
		}

		for (size_t i = 0; i < samplesToRead; ++i)
		{
			buffer[i] = g_audioCircularBuffer[g_audioReadIndex];
			g_audioReadIndex = (g_audioReadIndex + 1) % g_audioBufferSize;
		}

		if (samplesToRead > 0)
		{
			LogToUnity(LOG_DEBUG,
					   "[NativePlugin] Read %zu samples from audio buffer.",
					   samplesToRead);
		}

		return samplesToRead;
	}

	DLL_EXPORT int GetPluginID()
	{
		return 42;
	}

	DLL_EXPORT void SendGameData(const char* dataName, float value)
	{
		if (!g_isInitialized)
		{
			if (!g_errorLoggedSocketNotInit)
			{
				LogToUnity(
					LOG_ERROR,
					"[NativePlugin] Error: SendGameData called but VCVLink is "
					"not initialized. Call InitializeVCVLink first.");
				g_errorLoggedSocketNotInit = true;
			}
			return;
		}
		if (dataName == nullptr || *dataName == '\0')
		{
			// LogToUnity(LOG_ERROR, "[NativePlugin] Warning: SendGameData
			// called with null or empty dataName.");
			return;
		}

		std::lock_guard<std::mutex> guard(g_socketMutex);
		if (!g_transmitSocket)
			return;

		try
		{
			std::vector<char> buffer(OUTPUT_BUFFER_SIZE);
			osc::OutboundPacketStream p(buffer.data(), buffer.size());

			std::string oscAddress = "/telepath/";
			oscAddress += dataName;

			p << osc::BeginMessage(oscAddress.c_str()) << value
			  << osc::EndMessage;

			g_transmitSocket->Send(p.Data(), p.Size());

			// static int sendCount = 0;
			// if (sendCount++ % 100 == 0)
			// {
			//     std::cout << "[NativePlugin] Sent OSC: " << oscAddress <<
			//     " "
			//     << value << std::endl;
			// }
		}
		catch (const std::exception& e)
		{
			LogToUnity(LOG_ERROR,
					   "[NativePlugin] Error sending OSC message (%s): %s",
					   dataName, e.what());
		}
		catch (...)
		{
			LogToUnity(LOG_ERROR,
					   "[NativePlugin] Unknown error sending OSC message (%s).",
					   dataName);
		}
	}

	DLL_EXPORT bool IsTelepathChannelOpen()
	{
		return g_isInitialized && g_transmitSocket != nullptr;
	}

	DLL_EXPORT bool InitializeTelepath()
	{
		if (!InitializeWinsock())
			return false;

		std::lock_guard<std::mutex> guard(g_socketMutex);

		if (g_isInitialized)
		{
			LogToUnity(
				LOG_INFO,
				"[NativePlugin] InitializeVCVLink: Already initialized.");
			return true;
		}

		LogToUnity(
			LOG_INFO,
			"[NativePlugin] InitializeVCVLink: Attempting to initialize...");
		g_errorLoggedSocketNotInit = false;

		try
		{
			g_transmitSocket = new UdpTransmitSocket(
				IpEndpointName(VCV_RACK_IP, VCV_RACK_PORT));

			if (g_transmitSocket)
			{
				LogToUnity(
					LOG_INFO,
					"[NativePlugin] OSC UDP socket created, targeting %s : %d",
					VCV_RACK_IP, VCV_RACK_PORT);
				g_isInitialized = true;
				return true;
			}
			else
			{
				LogToUnity(LOG_ERROR,
						   "[NativePlugin] Error: Failed to create "
						   "UdpTransmitSocket object (returned null).");

				ShutdownWinsock();
				return false;
			}
		}
		catch (const std::exception& e)
		{
			LogToUnity(LOG_ERROR,
					   "[NativePlugin] Error initializing UDP socket: %s",
					   e.what());
			delete g_transmitSocket;
			g_transmitSocket = nullptr;
			g_isInitialized = false;

			ShutdownWinsock();
			return false;
		}
		catch (...)
		{
			LogToUnity(LOG_ERROR,
					   "[NativePlugin] Unknown error initializing UDP socket.");
			delete g_transmitSocket;
			g_transmitSocket = nullptr;
			g_isInitialized = false;

			ShutdownWinsock();
			return false;
		}
	}

	DLL_EXPORT void ShutdownTelepath()
	{
		std::lock_guard<std::mutex> guard(g_socketMutex);

		if (!g_isInitialized)
		{
			LogToUnity(LOG_INFO,
					   "[NativePlugin] ShutdownVCVLink: Already shut down or "
					   "never initialized.");
			return;
		}

		LogToUnity(LOG_INFO,
				   "[NativePlugin] ShutdownVCVLink: Shutting down...");

		if (g_transmitSocket)
		{
			delete g_transmitSocket;
			g_transmitSocket = nullptr;
			LogToUnity(LOG_INFO, "[NativePlugin] OSC UDP socket closed.");
		}
		g_isInitialized = false;

		ShutdownWinsock();
	}

	DLL_EXPORT bool InitializeTelepathListener(int port)
	{
		if (!InitializeWinsock())
			return false;

		if (g_listenerRunning)
		{
			if (port == g_listenPort)
			{
				LogToUnity(LOG_DEBUG,
						   "[NativePlugin] InitializeTelepathListener: Already "
						   "running on port %d",
						   port);
				return true;
			}
			else
			{
				LogToUnity(
					LOG_ERROR,
					"[NativePlugin] InitializeTelepathListener: Already "
					"running on different port %d. Please Shutdown first.",
					g_listenPort);

				ShutdownWinsock();
				return false;
			}
		}
		if (port <= 0 || port >= 65536)
		{
			LogToUnity(
				LOG_ERROR,
				"[NativePlugin] InitializeTelepathListener: Invalid port %d.",
				port);

			ShutdownWinsock();
			return false;
		}

		LogToUnity(LOG_INFO,
				   "[NativePlugin] InitializeTelepathListener: Attempting to "
				   "start on port %d...",
				   port);

		std::lock_guard<std::mutex> lock(g_messageQueueMutex);
		std::queue<ReceivedOscMessage> emptyQueue;
		std::swap(g_messageQueue, emptyQueue);

		g_listenPort = port;
		try
		{
			g_listenerThread = std::thread(ListenerThreadFunction, port);
			return true;
		}
		catch (const std::system_error& e)
		{
			LogToUnity(LOG_ERROR,
					   "[NativePlugin] Failed to create listener thread: %s",
					   e.what());
			g_listenerRunning = false;
			g_listenPort = 0;

			ShutdownWinsock();
			return false;
		}
		catch (...)
		{
			LogToUnity(
				LOG_ERROR,
				"[NativePlugin] Unknown error creating listener thread.");
			g_listenerRunning = false;
			g_listenPort = 0;

			ShutdownWinsock();
			return false;
		}
	}

	DLL_EXPORT void ShutdownTelepathListener()
	{
		if (!g_listenerRunning && !g_listenerThread.joinable())
		{
			LogToUnity(LOG_DEBUG,
					   "[NativePlugin] ShutdownTelepathListener: Listener not "
					   "running or already shut down.");
			return;
		}

		LogToUnity(
			LOG_INFO,
			"[NativePlugin] ShutdownTelepathListener: Stopping listener...");

		if (g_receiveSocket)
		{
			g_receiveSocket->AsynchronousBreak();
		}

		if (g_listenerThread.joinable())
		{
			g_listenerThread.join();
		}

		delete g_receiveSocket;
		g_receiveSocket = nullptr;
		g_listenerRunning = false;
		g_listenPort = 0;

		LogToUnity(LOG_INFO, "[NativePlugin] OSC Listener shut down complete.");
		std::lock_guard<std::mutex> lock(g_messageQueueMutex);
		std::queue<ReceivedOscMessage> emptyQueue;
		std::swap(g_messageQueue, emptyQueue);

		ShutdownWinsock();
	}

	DLL_EXPORT bool IsTelepathListenerRunning()
	{
		return g_listenerRunning;
	}

	DLL_EXPORT bool GetNextOscMessage(char* addressBuffer,
									  int addressBufferSize, float* outValue)
	{
		if (!g_listenerRunning && g_messageQueue.empty())
		{
			if (!g_errorLoggedListenerNotInit)
			{
				LogToUnity(
					LOG_DEBUG,
					"[NativePlugin] GetNextOscMessage: Listener not running.");
				g_errorLoggedListenerNotInit = true;
			}
			return false;
		}

		std::lock_guard<std::mutex> lock(g_messageQueueMutex);

		if (g_messageQueue.empty())
		{
			return false;
		}

		ReceivedOscMessage msg = g_messageQueue.front();
		g_messageQueue.pop();

		if (addressBuffer && addressBufferSize > 0)
		{
			strncpy(addressBuffer, msg.address.c_str(), addressBufferSize - 1);
			addressBuffer[addressBufferSize - 1] = '\0';
		}
		if (outValue)
		{
			*outValue = msg.value;
		}

		return true;
	}

	DLL_EXPORT void SetTelepathLogLevel(int level)
	{
		if (level >= LOG_DEBUG && level <= LOG_ERROR)
		{
			g_currentLogLevel = static_cast<LogLevel>(level);
			LogToUnity(LOG_INFO, "[NativePlugin] Log level set to %d", level);
		}
		else
		{
			LogToUnity(LOG_WARNING,
					   "[NativePlugin] Attempted to set invalid log level: %d",
					   level);
		}
	}

#ifdef __cplusplus
} // extern "C"
#endif