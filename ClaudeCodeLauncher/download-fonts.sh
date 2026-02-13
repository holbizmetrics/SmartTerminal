#!/bin/bash
# Download OpenSans fonts for ClaudeCodeLauncher
# Run this script before building if fonts are missing

FONTS_DIR="Resources/Fonts"
mkdir -p "$FONTS_DIR"

echo "Downloading OpenSans fonts..."

# OpenSans Regular
curl -L "https://github.com/googlefonts/opensans/raw/main/fonts/ttf/OpenSans-Regular.ttf" \
    -o "$FONTS_DIR/OpenSans-Regular.ttf"

# OpenSans Semibold  
curl -L "https://github.com/googlefonts/opensans/raw/main/fonts/ttf/OpenSans-SemiBold.ttf" \
    -o "$FONTS_DIR/OpenSans-Semibold.ttf"

echo "Done! Fonts downloaded to $FONTS_DIR"
ls -la "$FONTS_DIR"
