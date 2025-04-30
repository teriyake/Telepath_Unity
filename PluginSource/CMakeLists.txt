if(CMAKE_SYSTEM_NAME MATCHES "Darwin")
    set(CMAKE_OSX_ARCHITECTURES "arm64;x86_64" CACHE STRING "Build architectures for macOS")
    message(STATUS "Targeting macOS Architectures: ${CMAKE_OSX_ARCHITECTURES}")
endif()

cmake_minimum_required(VERSION 3.15)
project(Telepath LANGUAGES CXX)

set(CMAKE_CXX_STANDARD 17)
set(CMAKE_CXX_STANDARD_REQUIRED ON)
set(CMAKE_CXX_EXTENSIONS OFF)

#[====[
find_package(PkgConfig REQUIRED)
pkg_check_modules(OPUS REQUIRED opus)
if(OPUS_FOUND)
    message(STATUS "Found Opus: TRUE")
    message(STATUS "  Opus Includes: ${OPUS_INCLUDE_DIRS}")
    message(STATUS "  Opus Libraries: ${OPUS_LIBRARIES}")
else()
    message(FATAL_ERROR "Opus library not found by pkg-config. Please install opus (e.g., 'brew install opus') and ensure pkg-config can find it.")
endif()
]====]#

set(OSCPACK_DIR ${CMAKE_CURRENT_SOURCE_DIR}/vendor/oscpack)

set(OSCPACK_COMMON_SOURCES
    ${OSCPACK_DIR}/osc/OscOutboundPacketStream.cpp
    ${OSCPACK_DIR}/osc/OscTypes.cpp
    ${OSCPACK_DIR}/osc/OscReceivedElements.cpp
    ${OSCPACK_DIR}/ip/IpEndpointName.cpp
)

if(WIN32)
    message(STATUS "Configuring for Windows")
    set(OSCPACK_PLATFORM_SOURCES
        ${OSCPACK_DIR}/ip/win32/UdpSocket.cpp
        ${OSCPACK_DIR}/ip/win32/NetworkingUtils.cpp
    )
    add_definitions(-DNOMINMAX)
else()
    message(STATUS "Configuring for POSIX (Linux/macOS)")
    set(OSCPACK_PLATFORM_SOURCES
        ${OSCPACK_DIR}/ip/posix/UdpSocket.cpp
        ${OSCPACK_DIR}/ip/posix/NetworkingUtils.cpp
    )
endif()

set(OSCPACK_INCLUDE_DIR ${OSCPACK_DIR})

set(PLUGIN_SOURCES src/telepath.cpp)

add_library(${PROJECT_NAME} SHARED
    ${PLUGIN_SOURCES}
    ${OSCPACK_COMMON_SOURCES}
    ${OSCPACK_PLATFORM_SOURCES}
)

target_include_directories(${PROJECT_NAME} PUBLIC
    src
    ${OSCPACK_INCLUDE_DIR}
    # ${OPUS_INCLUDE_DIRS} 
)

#[====[
target_link_directories(${PROJECT_NAME} PUBLIC
    ${OPUS_LIBRARY_DIRS}
)

target_link_libraries(${PROJECT_NAME} PUBLIC
    ${OPUS_LIBRARIES} 
)

if(WIN32)
    target_link_libraries(${PROJECT_NAME} PUBLIC ws2_32)
endif()
]====]#

if(CMAKE_SYSTEM_NAME MATCHES "Darwin")
    set_target_properties(${PROJECT_NAME} PROPERTIES
        FRAMEWORK FALSE
		SUFFIX ".dylib"
        MACOSX_RPATH TRUE
    )
endif()

message(STATUS "Building ${PROJECT_NAME} library...")