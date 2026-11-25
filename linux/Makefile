# Harbor for Linux: harbord (the helper, .NET 8) and the GNOME Shell extension.
#
#   make                  build harbord (one native binary) and the extension zip into build/
#   make selftest         run harbord's self-test
#   make user-install     install for your user only (no root): ~/.local, then log out and back in
#   make install          install system-wide (packages use this with DESTDIR=...)
#   make packages         build .deb and .rpm with nfpm (into build/)
#   make smoke-test       run GNOME Shell headless with the extension and check it (needs gnome-shell)
#
# AOT=false builds harbord as a normal .NET app (needs the .NET 8 runtime) instead of a native binary.

PREFIX  ?= /usr
DESTDIR ?=
BINDIR  ?= $(PREFIX)/bin
LIBDIR  ?= $(PREFIX)/lib
DATADIR ?= $(PREFIX)/share
AOT     ?= true

UUID    := harbor@hritik-sharmaa.github.io
BUSNAME := io.github.hritik_sharmaa.Harbor
VERSION := $(shell sed -n 's/.*const string Version = "\(.*\)";.*/\1/p' ../src/Core/Platform.cs)
UNAME_M := $(shell uname -m)
RID     ?= $(if $(filter aarch64 arm64,$(UNAME_M)),linux-arm64,linux-x64)
ARCH    ?= $(if $(filter linux-arm64,$(RID)),arm64,amd64)
BUILD   := build
EXTSRC  := gnome-extension/$(UUID)
EXTZIP  := $(BUILD)/$(UUID).shell-extension.zip

.PHONY: all harbord extension selftest install uninstall user-install user-uninstall packages smoke-test clean version

all: harbord extension

version:
	@echo $(VERSION)

harbord:
	dotnet publish harbord/harbord.csproj -c Release -r $(RID) -p:Aot=$(AOT) \
	  $(if $(filter true,$(AOT)),,--self-contained false) -o $(BUILD)/harbord -nologo
	@rm -f $(BUILD)/harbord/*.dbg $(BUILD)/harbord/*.pdb

extension:
	@mkdir -p $(BUILD)
	rm -f $(EXTZIP)
	cd $(EXTSRC) && zip -qr $(CURDIR)/$(EXTZIP) . -x '*.swp'
	@echo "Built $(EXTZIP)"

selftest:
	dotnet run --project harbord/harbord.csproj -c Release -- --selftest

# -------------------------------------------------------------------- system-wide (packages)

install:
	@test -x $(BUILD)/harbord/harbord || { echo "Run 'make harbord' first"; exit 1; }
ifeq ($(AOT),true)
	install -Dm755 $(BUILD)/harbord/harbord $(DESTDIR)$(BINDIR)/harbord
else
	install -d $(DESTDIR)$(LIBDIR)/harbor
	cp -r $(BUILD)/harbord/. $(DESTDIR)$(LIBDIR)/harbor/
	install -d $(DESTDIR)$(BINDIR)
	ln -sf $(LIBDIR)/harbor/harbord $(DESTDIR)$(BINDIR)/harbord
endif
	install -d $(DESTDIR)$(DATADIR)/dbus-1/services
	sed 's|@BINDIR@|$(BINDIR)|' packaging/$(BUSNAME).service.in > $(DESTDIR)$(DATADIR)/dbus-1/services/$(BUSNAME).service
	install -d $(DESTDIR)$(DATADIR)/gnome-shell/extensions/$(UUID)
	install -m644 $(EXTSRC)/* $(DESTDIR)$(DATADIR)/gnome-shell/extensions/$(UUID)/
	install -Dm644 ../assets/Harbor.png $(DESTDIR)$(DATADIR)/icons/hicolor/256x256/apps/harbor.png

uninstall:
	rm -f $(DESTDIR)$(BINDIR)/harbord $(DESTDIR)$(DATADIR)/dbus-1/services/$(BUSNAME).service
	rm -rf $(DESTDIR)$(LIBDIR)/harbor $(DESTDIR)$(DATADIR)/gnome-shell/extensions/$(UUID)
	rm -f $(DESTDIR)$(DATADIR)/icons/hicolor/256x256/apps/harbor.png

# -------------------------------------------------------------------- just for you (no root)

USER_DATA := $(or $(XDG_DATA_HOME),$(HOME)/.local/share)
USER_BIN  := $(HOME)/.local/bin

user-install: harbord
	$(MAKE) install PREFIX=$(HOME)/.local BINDIR=$(USER_BIN) DATADIR=$(USER_DATA) LIBDIR=$(HOME)/.local/lib
	-gnome-extensions enable $(UUID) 2>/dev/null
	@echo
	@echo "Harbor is installed for $(USER). Log out and back in (GNOME on Wayland loads new extensions at login),"
	@echo "then turn it on if it isn't:  gnome-extensions enable $(UUID)"
	@echo "Ubuntu: turn Ubuntu Dock off so it doesn't sit on top of Harbor's dock:  gnome-extensions disable ubuntu-dock@ubuntu.com"

user-uninstall:
	-gnome-extensions disable $(UUID) 2>/dev/null
	$(MAKE) uninstall PREFIX=$(HOME)/.local BINDIR=$(USER_BIN) DATADIR=$(USER_DATA) LIBDIR=$(HOME)/.local/lib
	-pkill -x harbord
	@echo "Removed. Your settings stay in ~/.config/harbor."

# -------------------------------------------------------------------- .deb and .rpm

packages: harbord extension
	rm -rf $(BUILD)/stage
	$(MAKE) install DESTDIR=$(CURDIR)/$(BUILD)/stage PREFIX=/usr AOT=true
	VERSION=$(VERSION) ARCH=$(ARCH) nfpm package -f packaging/nfpm.yaml -p deb -t $(BUILD)/
	VERSION=$(VERSION) ARCH=$(ARCH) nfpm package -f packaging/nfpm.yaml -p rpm -t $(BUILD)/

smoke-test: harbord
	tests/shell-smoke.sh $(CURDIR)/$(BUILD)/harbord/harbord

clean:
	rm -rf $(BUILD) harbord/bin harbord/obj
