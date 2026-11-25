# Fedora / COPR package for Harbor's Linux side: harbord (NativeAOT, no .NET runtime needed) and the GNOME Shell
# extension. In COPR, turn on "Enable internet access during builds": dotnet restores Tmds.DBus.Protocol and the
# NativeAOT compiler from NuGet. Locally: `rpmbuild -ba harbor.spec` with the release tarball in SOURCES.
%global uuid harbor@hritik-sharmaa.github.io
# harbord is a stripped NativeAOT binary: there is no useful debuginfo to split out
%global debug_package %{nil}

Name:           harbor
Version:        0.4.2
Release:        1%{?dist}
Summary:        A macOS-style dock and live activities for GNOME Shell

License:        MIT
URL:            https://github.com/hritik-sharmaa/harbor
Source0:        %{url}/archive/v%{version}/%{name}-%{version}.tar.gz

ExclusiveArch:  x86_64 aarch64

BuildRequires:  dotnet-sdk-8.0
BuildRequires:  clang
BuildRequires:  zlib-devel
BuildRequires:  make
BuildRequires:  zip
# for %%check: harbord's self-test resolves time zones and formats dates through ICU
BuildRequires:  libicu
BuildRequires:  tzdata

Requires:       gnome-shell >= 45
Requires:       libicu
Recommends:     xdg-utils

%description
Harbor adds a macOS-style dock to GNOME Shell, with magnification, running
dots, launch bounce and Smart Hide, and live activities for timers, alarms,
reminders and meetings. Its helper, harbord, runs reminders, timers,
Pomodoro, Focus, weather, lyrics, calendar and Claude usage, and is started
by D-Bus when the extension first needs it.

%prep
%autosetup -n %{name}-%{version}

%build
export DOTNET_CLI_TELEMETRY_OPTOUT=1 DOTNET_NOLOGO=1 DOTNET_SKIP_FIRST_TIME_EXPERIENCE=1
make -C linux harbord AOT=true

%install
make -C linux install DESTDIR=%{buildroot} PREFIX=%{_prefix} BINDIR=%{_bindir} DATADIR=%{_datadir} AOT=true

%check
%{buildroot}%{_bindir}/harbord --selftest

%post
cat <<'MSG'
Harbor is installed. Log out and back in, then turn it on (as yourself, not root):
    gnome-extensions enable harbor@hritik-sharmaa.github.io
MSG

%files
%license LICENSE
%doc linux/README.md
%{_bindir}/harbord
%{_datadir}/dbus-1/services/io.github.hritik_sharmaa.Harbor.service
%{_datadir}/gnome-shell/extensions/%{uuid}/
%{_datadir}/icons/hicolor/256x256/apps/harbor.png

%changelog
* Wed Nov 25 2026 hritik-sharmaa <hritik-sharmaa@users.noreply.github.com> - 0.4.2-1
- First Linux release: Harbor's dock and live activities for GNOME Shell, and harbord
