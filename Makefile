TFM      := netstandard2.1
CONFIG   := Debug
DLL      := VGDataExport.dll

BUILDDIR := VGDataExport/bin/$(CONFIG)/$(TFM)
BUILDDLL := $(BUILDDIR)/$(DLL)

GAME_DIR        := /mnt/c/Program Files (x86)/Steam/steamapps/common/Vanguard Galaxy
PLUGIN_DIR      := $(GAME_DIR)/BepInEx/plugins
VGDATAEXPORT_DIR := $(PLUGIN_DIR)/VGDataExport

VGTTS_LIB := ../vanguard-galaxy-tts/VGTTS/lib

DOTNET ?= $(shell command -v dotnet 2>/dev/null || echo /tmp/dnsdk/dotnet/dotnet)
export DOTNET_ROLL_FORWARD := LatestMajor

.PHONY: all build link-asm deploy clean

all: build

link-asm:
	@mkdir -p VGDataExport/lib
	@if [ ! -e "VGDataExport/lib/Assembly-CSharp.dll" ]; then \
		ln -sf "$(abspath $(VGTTS_LIB))/Assembly-CSharp.dll" VGDataExport/lib/Assembly-CSharp.dll ; \
		echo "Linked Assembly-CSharp.dll from $(VGTTS_LIB)" ; \
	fi

build: link-asm
	DOTNET_ROOT=$(dir $(DOTNET)) $(DOTNET) build VGDataExport/VGDataExport.csproj -c $(CONFIG)

deploy: build
	@test -d "$(PLUGIN_DIR)" || { echo "BepInEx plugins dir not found at $(PLUGIN_DIR)" ; exit 1 ; }
	@mkdir -p "$(VGDATAEXPORT_DIR)"
	cp "$(BUILDDIR)"/*.dll "$(VGDATAEXPORT_DIR)/"
	@if [ -f "$(BUILDDIR)/VGDataExport.pdb" ]; then cp "$(BUILDDIR)/VGDataExport.pdb" "$(VGDATAEXPORT_DIR)/"; fi
	@echo "Deployed to $(VGDATAEXPORT_DIR)"

clean:
	-$(DOTNET) clean VGDataExport/VGDataExport.csproj
	rm -rf VGDataExport/bin VGDataExport/obj
