<div align="center">

# Triumvirate

### One window for DejaVu, Memento, and Recite.

`0.1.0` · `early`

</div>

---

## What it is

[DejaVu](https://github.com/blancodagoat/DejaVu), [Memento](https://github.com/blancodagoat/memento), and [Recite](https://github.com/blancodagoat/recite) each run fine on their own, tray icon and all. Triumvirate is for people who'd rather manage the three from one window than hunt three tray icons.

It's a front desk, not a merge. Under the hood the tools stay separate processes: separate crash domains, and DejaVu can still run elevated on its own for games that need it. Triumvirate just gives you one place to flip them on, update them, and change their settings.

## What it does

- **Enable / disable.** A toggle per tool. Turning one off sends its quit signal so it shuts down clean, not a kill.
- **Install and update.** Flip a toggle on a tool that isn't installed and Triumvirate pulls the latest release for it from GitHub. One "Update everything" button updates all three at once.
- **Settings, in one place.** Every tool's page edits that tool's own `config.json` directly, nothing proprietary, then restarts the tool so the change takes effect. Restarting DejaVu resets its in-flight buffer, so Triumvirate says so before you apply.

That's the whole app.

## Install

`Triumvirate.exe` from the [latest release](https://github.com/blancodagoat/triumvirate/releases/latest).

The three tools work fine without Triumvirate too, standalone or via `scoop bucket add blancodagoat https://github.com/blancodagoat/scoop-bucket` then `scoop install dejavu memento recite`.

## Building

```
dotnet publish src/Triumvirate/Triumvirate.csproj -c Release -p:SelfContained=false -o publish/framework-dependent
dotnet publish src/Triumvirate/Triumvirate.csproj -c Release -o publish/self-contained
```

Icons are generated, not drawn by hand: `python3 tools/make-icons.py`.

## Status

This is 0.1.0 and it is early: the three tools underneath are battle-tested, the window around them is days old. Expect rough edges and report them.

---

<div align="center">

**[MIT license](LICENSE)** · sibling of [DejaVu](https://github.com/blancodagoat/DejaVu), [Memento](https://github.com/blancodagoat/memento), and [Recite](https://github.com/blancodagoat/recite)

</div>
