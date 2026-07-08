# Aqroflow

Local, free voice dictation for Windows — a Wispr Flow style tool with zero cloud, zero tokens, zero cost.

A small floating mic button sits on your desktop. Click it, speak, and your words are typed into whatever app you last clicked — editor, browser, chat, terminal. Transcription runs fully on-device.

## How it works

1. Trigger dictation — **hold Right Ctrl** (default), or click the floating mic. The widget never steals focus, so your caret stays where it was.
2. Speak — release the key to stop (push-to-talk), or in click/toggle mode it auto-stops after ~1.8s of silence.
3. Whisper (tiny.en, int8) transcribes locally in under a second.
4. Text is pasted at your caret via clipboard + Ctrl+V (your old clipboard is restored).

## Hotkey

Right-click the widget to switch between:

- **Push-to-talk** (default) — hold the key, speak, release. Best for short bursts.
- **Toggle** — tap to start, tap again to stop. Best for long dictation.

Default key is **Right Ctrl**. Spacebar alone can't be used — it's needed for typing everywhere. To change the key, edit `%APPDATA%\Aqroflow\settings.json`:

```json
{ "HotkeyVk": 163, "HotkeyMode": "pushToTalk" }
```

`HotkeyVk` is a Windows virtual-key code (163 = Right Ctrl, 162 = Left Ctrl, 165 = Right Alt, 121 = F10, 19 = Pause, 145 = Scroll Lock).

## States

| Color | Meaning |
|---|---|
| Gray | Loading / downloading model (first run only, ~32MB) |
| Teal→blue | Idle, click to dictate |
| Red, pulsing | Recording |
| Purple, spinner | Transcribing |

Drag the button anywhere — position is remembered. Right-click → Exit to quit.

## Tech

- C# / WPF, .NET 8 — native Windows, low footprint (~50MB idle, ~200MB transcribing)
- [Whisper.net](https://github.com/sandrohanea/whisper.net) (whisper.cpp bindings), `ggml-tiny.en-q5_1` model
- NAudio for 16kHz mic capture with RMS-based silence detection
- `WS_EX_NOACTIVATE` window so the widget never takes focus from your target app

Model and settings live in `%APPDATA%\Aqroflow\`.

## Build

```
dotnet build Aqroflow\Aqroflow.csproj -c Release
```

## Roadmap

- Settings UI (hotkey push-to-talk, model picker, language)
- Larger Whisper models / NVIDIA Parakeet V3 via sherpa-onnx as opt-in "accurate" mode
- Optional local LLM cleanup pass (Ollama) for filler removal and formatting
- Auto-hide at idle, waveform visual
