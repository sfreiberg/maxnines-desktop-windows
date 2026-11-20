# MaxNines Desktop for Windows

A Windows notification area app for [MaxNines](https://github.com/sfreiberg/maxnines).
It shows the alerts that are firing on your MaxNines server and links to its web
interface. It's the Windows version of MaxNines Desktop for Mac, a menu bar app, and
does the same things.

## Installing

Download the installer for your computer from
[Releases](https://github.com/sfreiberg/maxnines-desktop-windows/releases): `x64` for most PCs,
`arm64` for ARM ones such as Surface Pro X and Copilot+ PCs. It installs for you alone,
in your own AppData, so it doesn't need an administrator, and adds MaxNines to the Start
menu. **Start MaxNines when I sign in** is ticked to begin with, and the app's own
**Open at login** switch changes it later.

The installer isn't signed yet, so Windows may say it protected your PC. Choose **More
info**, then **Run anyway**.

Installing a newer version, or the same version's installer for the other kind of PC,
replaces the one installed and keeps your settings and sign in.
To remove it, use **Settings > Apps > Installed apps**, which also stops it starting when
you sign in, signs it out of your server, and removes its settings. For an install with
no questions:

```powershell
msiexec /i maxnines-desktop-0.1.0-alpha.6-x64.msi /qn                    # starts at sign in
msiexec /i maxnines-desktop-0.1.0-alpha.6-x64.msi /qn START_AT_LOGIN=0   # doesn't
```

## Setting it up

1. Open MaxNines from the Start menu if it isn't running. Its settings open the first
   time, and whenever you open it from the Start menu while it's running. You can
   also click its icon and sign in from there.
2. Enter the address you open MaxNines at, and choose **Sign in with browser**.
3. Your browser opens MaxNines. Sign in there if you aren't already, choose a role for
   the app, and choose **Allow**. A viewer sees alerts. A responder can also
   acknowledge and silence them. You can't give the app more than your own role.
4. Back in the app's settings, it says who it's signed in as and with which role.

It stays signed in until it goes 90 days without being used. Only its refresh token is kept, in Windows Credential Manager. To sign
it out, choose **Sign out** in its settings. In MaxNines, **Settings > Signed-in apps**
lists it as MaxNines Desktop for Windows, with this computer's name, and can revoke it from
there too. If it's signed out, it says **Signed out: sign in again**, and clicking its
icon offers **Sign in** to sign in again straight away.

Later, right-click the icon and choose **Settings**. To change the server, sign out first.

A plain `http` address only works for a server on your local network: this computer, a
private address such as `192.168.0.155`, or a name with no dots or ending in `.local`.
Anywhere else needs `https`, so signing in never crosses the internet unencrypted.

## What it shows

The icon says the worst thing happening: plain when nothing is firing, with an amber
square when there are only warnings, and with a red dot when something is critical.
Dimmed and dashed, it can't reach the server, so quiet is never mistaken for fine.
Pointing at it says how many alerts are firing.

Clicking it shows how many hosts are online, the alerts firing, with Acknowledge and
Silence 1 hour, what resolved in the last hour, and the silences running now. If the
server runs checks itself, it also shows how many of them are passing, counting only
checks that aren't paused and have run since the server started.

## Notifications

It can tell you when a critical alert starts, when a warning starts, and when an alert
resolves, each turned on or off in its settings. All three are on to begin with. Alerts
that a silence or maintenance window is holding back never send one, as everywhere else
in MaxNines, and one still firing when its silence ends sends one then. Nothing that
was already firing when it started is announced, and more than three at once arrive as
one.

Each starts with the circle MaxNines uses in Discord: red for critical, yellow for a
warning, and green once it's resolved. A resolved one says where things stand, such as
"web-1 is back online", and how long the problem lasted.

A notification about an alert that's started has Acknowledge, which needs the
responder role, and Open in MaxNines. Clicking one after you've quit MaxNines starts it
again to do what it says. Each acts on the server the alert came from: once you've
signed in to another server, Open in MaxNines still opens the first one's page, and
Acknowledge says it can't rather than acknowledge another server's alert.

## Open at login

Turn on **Open at login** in its settings. Leave the program where it is afterwards, as
Windows starts it from where it was when you turned it on.

## Building

It needs Windows 10 version 1809 or later, and the .NET 10 SDK.

```powershell
dotnet test --project tests/MaxNinesKit.Tests
dotnet publish src/MaxNinesTray -c Release -r win-x64 -p:Platform=x64 -o out
out\MaxNines.exe
```

Use `win-arm64` and `ARM64` for ARM computers. The Windows App SDK is carried in the
`out` folder, so nothing else needs installing.

The bottom of its settings says exactly which build it is: the version, the commit it
was built from, ending in `-dirty` if there were uncommitted changes, and when it was
built. Building needs `git` for the commit; without it, the commit shows as `unknown`.

## License

MIT. See [LICENSE](LICENSE).
