PREFIX ?= $(HOME)/.local
VERSION := $(shell sed -n 's/^public let plugviewVersion = "\(.*\)"/\1/p' Sources/PlugviewCore/Constants.swift)
DIST_DIR := .build/dist
# scripts/release.sh publishes these under versioned names, plus version-free copies in plugview.app/releases/latest/.
ARCHIVE := $(DIST_DIR)/plugview-macos-universal.tar.gz
STRICT := -Xswiftc -warnings-as-errors
UNIVERSAL := swift build -c release --arch arm64 --arch x86_64
APP := $(DIST_DIR)/plugview.app
APP_ARCHIVE := $(DIST_DIR)/plugview-macos-app.zip
ICON := Sources/plugview/Resources/AppIcon.icns
DESIGN_DIR ?= ../plugview-design

.PHONY: build test lint format check install uninstall dist app icon version clean

build:
	swift build -c release

test:
	swift test $(STRICT)

lint:
	swift format lint -r --strict Sources Tests Package.swift

format:
	swift format -i -r Sources Tests Package.swift

# Everything CI runs: warnings are errors, lint is strict, tests pass.
check: lint
	swift build -c release $(STRICT)
	swift test $(STRICT)

install: build
	install -d $(PREFIX)/bin
	install .build/release/plugview $(PREFIX)/bin/plugview

uninstall:
	rm -f $(PREFIX)/bin/plugview

# Release archives, both universal (Apple silicon + Intel), each with its SHA-256 checksum: the bare command
# (Homebrew formula, curl install) and plugview.app (Homebrew cask, browser download).
dist: app
	rm -rf $(DIST_DIR)/plugview-$(VERSION) $(ARCHIVE)* $(APP_ARCHIVE)* && mkdir -p $(DIST_DIR)/plugview-$(VERSION)
	cp "$$($(UNIVERSAL) --show-bin-path)/plugview" LICENSE README.md CHANGELOG.md $(DIST_DIR)/plugview-$(VERSION)/
	tar -czf $(ARCHIVE) -C $(DIST_DIR) plugview-$(VERSION)
	ditto -c -k --keepParent $(APP) $(APP_ARCHIVE)
	cd $(DIST_DIR) && shasum -a 256 $(notdir $(ARCHIVE)) > $(notdir $(ARCHIVE)).sha256
	cd $(DIST_DIR) && shasum -a 256 $(notdir $(APP_ARCHIVE)) > $(notdir $(APP_ARCHIVE)).sha256
	@cat $(ARCHIVE).sha256 $(APP_ARCHIVE).sha256

# plugview.app: the same universal binary, which still works from the terminal as Contents/MacOS/plugview.
app:
	$(UNIVERSAL) $(STRICT)
	rm -rf $(APP) && mkdir -p $(APP)/Contents/MacOS $(APP)/Contents/Resources
	cp Sources/plugview/Resources/Info.plist $(APP)/Contents/
	cp $(ICON) $(APP)/Contents/Resources/
	bin="$$($(UNIVERSAL) --show-bin-path)" && cp "$$bin/plugview" $(APP)/Contents/MacOS/ && cp -R "$$bin/plugview_PlugviewUI.bundle" $(APP)/Contents/Resources/
	codesign --force --sign - $(APP)
	@echo $(APP)

# Regenerates the committed AppIcon.icns from the brand kit (needs rsvg-convert and ImageMagick).
# The brand tile fills 480 of 512 px; a macOS icon tile fills 824 of 1024, so it is drawn at 103/120 and centered.
icon:
	rm -rf $(DIST_DIR)/AppIcon.iconset && mkdir -p $(DIST_DIR)/AppIcon.iconset
	for size in 16 32 128 256 512; do for scale in 1 2; do \
		px=$$((size * scale)); name=icon_$${size}x$${size}; [ $$scale = 1 ] || name=$${name}@2x; \
		rsvg-convert -w $$((px * 103 / 120)) $(DESIGN_DIR)/logo/svg/plugview-icon.svg \
			| magick - -background none -gravity center -extent $${px}x$${px} $(DIST_DIR)/AppIcon.iconset/$$name.png || exit 1; \
	done; done
	iconutil -c icns -o $(ICON) $(DIST_DIR)/AppIcon.iconset

version:
	@echo $(VERSION)

clean:
	rm -rf .build
