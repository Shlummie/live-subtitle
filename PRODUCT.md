# Product

<!-- impeccable:product-schema 1 -->

## Platform

windows

## Stack

Native Windows x64 desktop application using .NET 10 and WPF, targeting Windows 10 version 2004 (build 19041) or newer. Release builds are self-contained.

## Users

The primary user runs games, videos, and calls on a Windows PC and wants fast, readable English subtitles without sending audio or transcripts to a cloud service.

## Product Purpose

LiveSubtitle turns audio already playing on the PC, audio from a selected application, or audio from a microphone or other input into near-instant local English captions. Success means captions remain useful during long sessions, the active source is always clear, and unsaved speech leaves no transcript or raw-audio artifact behind.

## Positioning

LiveSubtitle is a source-aware local caption layer: it can caption several Windows audio sources independently, preserve their labels and ordering, and render them in a customizable desktop overlay without cloud processing, accounts, or game-process injection.

## Operating Context

- Windows 10 desktop, often while a game, video player, browser, or call application is running.
- Windowed and borderless-fullscreen applications; true exclusive-fullscreen injection is out of scope.
- One or more render endpoints, process audio streams, microphones, or line inputs.
- RTX 5070 Ti Vulkan acceleration with an AVX2 CPU fallback on the current PC.
- A separate control center, transparent always-on-top caption overlay, and system-tray controls.

## Capabilities and Constraints

- English transcription only; no translation and no within-source speaker diarization.
- Whole-output, microphone/input, and per-process-tree capture on supported Windows builds.
- Simultaneous sources are transcribed independently and can be labeled and colored separately.
- Aggressive live drafts are the default; users can select balanced or stable presets, advanced timing controls, and per-profile audio sensitivity.
- Every configurable control exposes concise inline help through an **i** button that works with hover, keyboard focus, click, and screen readers.
- Blank-audio/no-speech sentinel output is discarded before it reaches the transcript, overlay, or saved-session pipeline.
- The portable release is one compressed, self-contained x64 executable; models remain separate explicit downloads.
- Models run locally. No telemetry, accounts, remote inference, or automatic transcript recording.
- Live text is memory-only unless the user enables Save this session. Raw audio is never stored.
- Protected audio, unusual helper-process routing, and browser multi-process behavior may require whole-output fallback.
- Fully customizable means GUI settings, named profiles, global hotkeys, theme and overlay controls, and versioned JSON import/export. It does not include a plugin SDK or custom CSS.

## Brand Commitments

- Product name: LiveSubtitle.
- Familiar Windows utility, calibrated against Microsoft PowerToys and Windows Settings.
- Restrained, task-first presentation with a dark default, plus light, system-following, and high-contrast modes.
- Clear, factual language; no AI hype, gamification, or hidden listening state.

## Evidence on Hand

- Current machine: Windows 10 build 19045, Intel Core i7-14700K, NVIDIA GeForce RTX 5070 Ti with 16 GB VRAM, and Vulkan 1.4.321.
- No existing code, logo, customer claims, benchmarks, or visual assets existed before this build.

## Product Principles

1. Listening is always explicit and visible.
2. Drafts arrive quickly, but provisional text never masquerades as final text.
3. Audio and transcripts stay local and ephemeral unless the user deliberately saves them.
4. Source routing and failures are understandable without audio-engine expertise.
5. Deep customization lives behind approachable presets and reversible defaults.

## Accessibility & Inclusion

The entire control center must support keyboard navigation, screen-reader naming, Windows high contrast, DPI scaling, reduced motion, and status communication that does not depend on color alone. Captions must remain readable against changing video and game content.
