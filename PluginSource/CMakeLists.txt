if(CMAKE_SYSTEM_NAME MATCHES "Darwin")
    set(CMAKE_OSX_ARCHITECTURES "arm64;x86_64" CACHE STRING "Build architectures for macOS")
    message(STATUS "Targeting macOS Architectures: ${CMAKE_OSX_ARCHITECTURES}")
endif()

cmake_minimum_required(VERSION 3.15)
project(Telepath LANGUAGES CXX)

set(CMAKE_CXX_STANDARD 17)
set(CMAKE_CXX_STANDARD_REQUIRED ON)
set(CMAKE_CXX_EXTENSIONS OFF)

find_package(PkgConfig REQUIRED)

pkg_check_modules(OPUS REQUIRED opus)
if(OPUS_FOUND)
    message(STATUS "Found Opus: TRUE")
    message(STATUS "  Opus Includes: ${OPUS_INCLUDE_DIRS}")
    message(STATUS "  Opus Libraries: ${OPUS_LIBRARIES}")
else()
    message(FATAL_ERROR "Opus library not found by pkg-config. Please install opus (e.g., 'brew install opus') and ensure pkg-config can find it.")
endif()

set(OSCPACK_DIR vendor/oscpack)

set(OSCPACK_SOURCES
    ${OSCPACK_DIR}/ip/posix/UdpSocket.cpp
    ${OSCPACK_DIR}/osc/OscOutboundPacketStream.cpp
    ${OSCPACK_DIR}/osc/OscTypes.cpp
	${OSCPACK_DIR}/osc/OscReceivedElements.cpp
    ${OSCPACK_DIR}/ip/posix/NetworkingUtils.cpp
    ${OSCPACK_DIR}/ip/IpEndpointName.cpp
)

set(OSCPACK_INCLUDE_DIR ${OSCPACK_DIR})

set(PLUGIN_SOURCES src/telepath.cpp)

add_library(${PROJECT_NAME} SHARED
    ${PLUGIN_SOURCES}
    ${OSCPACK_SOURCES}
)

target_include_directories(${PROJECT_NAME} PUBLIC
    src
    ${OSCPACK_INCLUDE_DIR}
    ${OPUS_INCLUDE_DIRS} 
)

target_link_directories(${PROJECT_NAME} PUBLIC
    ${OPUS_LIBRARY_DIRS}
)

target_link_libraries(${PROJECT_NAME} PUBLIC
    ${OPUS_LIBRARIES} 
)

if(CMAKE_SYSTEM_NAME MATCHES "Darwin")
    set_target_properties(${PROJECT_NAME} PROPERTIES
        FRAMEWORK FALSE
        BUNDLE TRUE
        BUNDLE_EXTENSION "bundle"
        MACOSX_RPATH TRUE
    )

    #set_target_properties(${PROJECT_NAME} PROPERTIES
        #CXX_VISIBILITY_PRESET hidden
        #VISIBILITY_INLINES_HIDDEN YES
    #)

    # include(GNUInstallDirs)
    # install(TARGETS ${PROJECT_NAME}
    #     LIBRARY DESTINATION ${CMAKE_INSTALL_LIBDIR}
    #     BUNDLE DESTINATION ${CMAKE_INSTALL_BINDIR}/../Plugins
    # )
endif()

message(STATUS "Building ${PROJECT_NAME} library...")