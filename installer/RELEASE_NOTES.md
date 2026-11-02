## تازه‌های نسخه ۱.۴ / What's new in 1.4

**فارسی**
- **عامل پس‌زمینه:** کنار ساعت ویندوز اجرا می‌شود، بازی را خودکار تشخیص می‌دهد، بهینه‌سازی‌ها را اعمال می‌کند و بعد از بستن بازی همه‌چیز را برمی‌گرداند. حتی وقتی پنجره‌ی برنامه بسته است کار می‌کند.
- **محدودسازی برنامه‌های پس‌زمینه در زمان بازی:** مرورگرها، همگام‌سازها و لانچرها حالت EcoQoS می‌گیرند. چت صوتی، ابزارهای ضبط و برنامه‌های امنیتی هرگز محدود نمی‌شوند.
- **اولویت هسته‌های قدرتمند** در پردازنده‌های هیبریدی Intel. این گزینه اختیاری است.
- **آزمون خودکار A/B:** فقط بهبودی را گزارش می‌کند که از نوسان طبیعی اجراها بزرگ‌تر باشد.
- **بررسی‌های جدید:** MPO خاموش، یکپارچگی حافظه (VBS) و پردازنده‌ی هیبریدی.
- **لوگوی جدید ProSyS.**

**English**
- **Background agent:** runs in the system tray, detects games, optimizes them and restores everything when they close, even with the control center closed.
- **Background EcoQoS while playing** for browsers, sync clients and launchers. Voice chat, recording tools and security software are never touched.
- **Prefer performance cores** on hybrid Intel CPUs (opt-in).
- **Automatic A/B test:** reports an improvement only when it is larger than run-to-run noise.
- **New advisor checks:** MPO disabled, Memory Integrity (VBS), hybrid CPU.
- **New ProSyS logo.**

## دانلود و نصب / Download and install

**فارسی**
1. فایل `ProSyS-Gaming-Optimizer-<نسخه>-Setup.exe` را از بخش Assets همین صفحه دانلود و اجرا کنید.
2. مراحل نصب را تأیید کنید. همه‌چیز لازم (‎.NET، PresentMon و ابزار ساخت نقطه بازیابی) داخل نصب‌کننده است؛ لازم نیست چیزی جداگانه نصب کنید یا دستوری در PowerShell اجرا کنید.
3. برنامه را از منوی Start یا میان‌بر دسکتاپ باز کنید.

برای بنچمارک FPS بدون دسترسی مدیر، گزینه مربوط در نصب‌کننده به‌صورت پیش‌فرض فعال است؛ فقط یک بار از حساب کاربری خارج و دوباره وارد شوید.
نسخه قابل‌حمل (`Portable-x64.zip`) هم بدون نصب اجرا می‌شود؛ در این حالت بنچمارک به اجرای برنامه با دسترسی مدیر نیاز دارد.

**English**
1. Download `ProSyS-Gaming-Optimizer-<version>-Setup.exe` from the Assets below and run it.
2. Everything the app needs (.NET runtime, PresentMon, the restore-point helper) is bundled. No separate downloads and no PowerShell commands.
3. Open ProSyS from the Start menu or the desktop shortcut.

The installer's "Allow FPS benchmarking without administrator rights" option is on by default; sign out and back in once for it to take effect. The portable ZIP runs without installation, but benchmarking then requires running ProSyS as administrator.

> The installer is not Authenticode-signed yet, so Windows SmartScreen may show "Windows protected your PC". Choose **More info → Run anyway**. Verify the download against `SHA256SUMS.txt` if you want to be sure it is unmodified.

**Requirements:** Windows 11 (build 22000+), x64.

See `CHANGELOG.md` in this release's source code for the full list of changes.
