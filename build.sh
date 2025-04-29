#!/bin/bash

clear
cd PluginSource
rm -rf build
mkdir -p build
cd build
cmake ..
make
cd ..
cd ..