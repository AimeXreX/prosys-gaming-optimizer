# Benchmarking

ProSyS 1.0 includes a local PresentMon 2.6.0 frame-time capture workflow. The bundled binary is accepted only when both its pinned SHA-256 digest and Intel Authenticode signing identity match.

The application reports average FPS, 1% low, 0.1% low, median and P99 frame time, standard deviation and frame count. A/B comparison rejects different game processes and lowers confidence when the Windows build or GPU driver changed.

For useful results, use the same save, scene, route, resolution, graphics settings, frame cap, power plan and background workload. Run a warm-up first and capture at least three repeated trials. ProSyS does not claim that a registry preference improves performance unless the result is measured on the target machine.

Current limitation: scene automation, automatic outlier rejection and confidence intervals are planned for a later release; the UI therefore presents observed values rather than guaranteed gains.
