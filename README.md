# MaxNines for Windows

A Windows notification area app for [MaxNines](https://github.com/sfreiberg/maxnines).
It shows the alerts that are firing on your MaxNines server and links to its web
interface. It's the Windows version of the Mac menu bar app, and does the same things.

## Setting it up

1. In MaxNines, open **Settings** and make an API key. A viewer key shows alerts. A
   responder key can also acknowledge and silence them.
2. Start `MaxNines.exe`. Its settings open the first time: enter your server's address
   and the key. The key is kept in Windows Credential Manager.

Later, right-click the icon and choose **Settings**.

A plain `http` address only works for a server on your local network: this computer, a
private address such as `192.168.0.155`, or a name with no dots or ending in `.local`.
Anywhere else needs `https`, so the key never crosses the internet unencrypted.

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
resolves, each turned on or off in its settings. Critical alerts and resolved ones are
on to begin with. Alerts that a silence or maintenance window is holding back never send
one, as everywhere else in MaxNines. Nothing that was already firing when it started is
announced, and more than three at once arrive as one.

A notification about an alert that's started has Acknowledge, which needs a responder
key, and Open in MaxNines.

## Open at login

Turn on **Open at login** in its settings. Leave the program where it is afterwards, as
Windows starts it from where it was when you turned it on.

## Building

It needs Windows 10 version 1809 or later, and the .NET 10 SDK.

```powershell
dotnet test tests/MaxNinesKit.Tests
dotnet publish src/MaxNinesTray -c Release -r win-x64 -p:Platform=x64 -o out
out\MaxNines.exe
```

Use `win-arm64` and `ARM64` for ARM computers. The Windows App SDK is carried in the
`out` folder, so nothing else needs installing.
