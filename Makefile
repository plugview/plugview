PREFIX ?= $(HOME)/.local
VERSION := $(shell sed -n 's/^public let plugviewVersion = "\(.*\)"/\1/p' Sources/PlugviewCore/Constants.swift)
DIST_DIR := .build/dist
# Version-free name, so https://github.com/<owner>/plugview/releases/latest/download/<name> always works.
ARCHIVE := $(DIST_DIR)/plugview-macos-universal.tar.gz
STRICT := -Xswiftc -warnings-as-errors

.PHONY: build test lint format check install uninstall dist version clean

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

# Universal (Apple silicon + Intel) release archive with its SHA-256 checksum.
dist:
	swift build -c release --arch arm64 --arch x86_64 $(STRICT)
	rm -rf $(DIST_DIR) && mkdir -p $(DIST_DIR)/plugview-$(VERSION)
	cp "$$(swift build -c release --arch arm64 --arch x86_64 --show-bin-path)/plugview" LICENSE README.md CHANGELOG.md $(DIST_DIR)/plugview-$(VERSION)/
	tar -czf $(ARCHIVE) -C $(DIST_DIR) plugview-$(VERSION)
	cd $(DIST_DIR) && shasum -a 256 $(notdir $(ARCHIVE)) > $(notdir $(ARCHIVE)).sha256
	@cat $(ARCHIVE).sha256

version:
	@echo $(VERSION)

clean:
	rm -rf .build
