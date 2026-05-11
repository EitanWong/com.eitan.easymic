# EasyMic Pipeline Visualizer

The EasyMic Pipeline Visualizer is a Unity Editor inspection tool for understanding live capture, playback, DSP, queue, and thread topology. It is available from **Window > EasyMic > Pipeline Visualizer**.

## Architecture

- Runtime systems expose immutable diagnostic snapshots only. Editor code never runs inside audio callbacks and the runtime assembly does not reference editor APIs.
- `AudioPipeline.GetProcessorSnapshots()` exports the current atomic worker snapshot with processor order, enabled state, node kind, and thread classification.
- `MicSystem.GetRecordingPipelineSnapshots()` exports active capture sessions, device format, latency profile, callback counters, queue telemetry, and capture DSP stages.
- `AudioSystem.PipelineSnapshot` exports playback device state, master mixer topology, source queues, mixer/source DSP stages, and render transport telemetry.
- The editor converts those snapshots into a separate visual graph model. The visual graph is not serialized and is not used to drive runtime behavior.

## Update Model

- Telemetry refresh is throttled to 15 Hz.
- Topology refresh is throttled to 2 Hz and guarded by a topology hash.
- Graph nodes are rebuilt only when topology changes or the user requests a snapshot.
- Existing node views receive telemetry updates without rebuilding the graph, which avoids layout churn and repaint storms.
- Telemetry updates only touch labels and lightweight visual state when values actually change.
- Graph mutation callbacks, paste, delete, and edge creation are disabled so the view remains inspection-only.
- Initial framing uses model bounds and waits for a valid GraphView layout before applying the viewport transform.
- Direction markers and optional flow pulses are separate retained graph elements. They update a phase value at a throttled rate and do not rebuild edges.
- Playback and Recording use separate GraphView canvases. Switching canvases only changes which retained graph is visible.

## Visual Model

- Left to right flow represents signal direction.
- Orange-highlighted edges indicate queue, native, or thread boundaries.
- GraphView groups separate playback and recording pipelines without turning the tool into an authoring graph. Group bounds are derived from child node positions so nodes stay visually contained as topology changes.
- Node borders encode thread ownership:
  - orange: native realtime boundary
  - blue: realtime audio callback
  - green: managed worker thread
  - violet: Unity main thread
  - yellow: telemetry thread
- Capture pipelines show device input, capture queue, managed transport, ordered DSP stages, and consumers.
- Playback pipelines show sources, source queues, source DSP, mixer hierarchy, playback transport, render queue, and output device.
- When no capture session is active, the capture section stays visible as an idle state so recording support is discoverable.
- Use **Playback** and **Recording** canvases to inspect output and input pipelines separately without changing runtime topology.

## Workflow

- Use **Playback** or **Recording** to switch between the two dedicated canvases.
- Select a node to inspect its current metrics and thread ownership in the side panel.
- Drag the minimap to a corner to dock it. The visualizer stores the dock enum, then recomputes the minimap rectangle from the current GraphView viewport on resize or editor docking changes.
- Use the inspector dock command, or drag the inspector header toward the window edge, to dock the runtime panel left or right.
- Use **Flow** to enable or disable animated edge pulses.
