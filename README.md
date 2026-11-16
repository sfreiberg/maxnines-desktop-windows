# MaxNines for Windows

A Windows notification area app for [MaxNines](https://github.com/sfreiberg/maxnines).
It shows the alerts that are firing on your MaxNines server and links to its web
interface. It's the Windows version of the Mac menu bar app, and does the same things.

## Setting it up

1. Start `MaxNines.exe`. Its settings open the first time.
2. Enter the address you open MaxNines at, and choose **Sign in with browser**.
3. Your browser opens MaxNines. Sign in there if you aren't already, choose a role for
   the app, and choose **Allow**. A viewer sees alerts. A responder can also
   acknowledge and silence them. You can't give the app more than your own role.
4. Back in the app's settings, it says who it's signed in as and with which role.

It stays signed in until it goes 90 days without being used. Only its refresh token is kept, in Windows Credential Manager. To sign
it out, choose **Sign out** in its settings. In MaxNines, **Settings > Signed-in apps**
lists it as MaxNines for Windows, with this computer's name, and can revoke it from
there too. If it's signed out, it says **Signed out: sign in again**.

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
resolves, each turned on or off in its settings. Critical alerts and resolved ones are
on to begin with. Alerts that a silence or maintenance window is holding back never send
one, as everywhere else in MaxNines, and one still firing when its silence ends sends one
then. Nothing that was already firing when it started is announced, and more than three
at once arrive as one.

A notification about an alert that's started has Acknowledge, which needs the
responder role, and Open in MaxNines.

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
