# Benchmarking

ProSyS includes a local PresentMon 2.6.0 frame-time capture workflow. The bundled binary is accepted only when its SHA-256 digest matches the pinned official release (the trust anchor); the embedded signer name must also be Intel Corporation. The Authenticode certificate chain is not validated by the app — the release pipeline does that when it downloads the binary.

The application reports average FPS, 1% low, 0.1% low, median and P99 frame time, standard deviation and frame count. Mark runs taken before a change as **Baseline run**. Comparison needs at least three baseline and three after runs of the same game with the same capture duration; it reports the change in average FPS and 1% low with a Welch 95% confidence interval and calls a result an improvement or a **regression** only when that interval excludes zero. Runs with a different Windows build or GPU driver are reported as not comparable.

For useful results, use the same save, scene, route, resolution, graphics settings, frame cap, power plan and background workload. Run a warm-up first and capture at least three repeated trials. ProSyS does not claim that a registry preference improves performance unless the result is measured on the target machine.

Current limitation: scene automation and automatic outlier rejection are not implemented; a noisy scene widens the confidence interval, which is reported as "no significant change" rather than a gain.
