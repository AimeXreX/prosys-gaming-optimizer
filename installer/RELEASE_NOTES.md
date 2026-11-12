## چه چیزی جدید است در ۱.۵ / What's new in 1.5

**فارسی**
- **ظاهر کاملاً جدید:** تم تیره‌ی نئونی با رنگ‌های فیروزه‌ای و بنفش، دکمه‌های درخشان، کارت‌های گرد، پنل اصلی جدید در داشبورد و سوییچ‌های روشن/خاموش به‌جای چک‌باکس.
- **انتخاب همه:** در صفحه‌ی مرکز بهینه‌سازی می‌توانید همه‌ی بهینه‌سازی‌های قابل اعمال را با یک کلیک انتخاب یا انتخاب را پاک کنید. شمارنده‌ی «N از M مورد انتخاب شده» هم نمایش داده می‌شود.
- فیلد جستجو، منوی کشویی، لیست‌ها و جدول‌ها هم با سبک جدید هماهنگ شدند و اورلی بازی از همان رنگ فیروزه‌ای استفاده می‌کند.
- هیچ رفتار یا مسیر پشتیبان‌گیری/بازگردانی تغییر نکرده است.

**English**
- **Brand-new look:** deeper dark theme with neon teal and violet accents, glowing buttons, rounded cards, a new dashboard hero panel and on/off toggle switches instead of plain checkboxes.
- **Select all:** the Recommendations page can now select every available optimization (or clear the selection) in one click, with a live "N of M selected" counter. It respects search and filters and skips incompatible or already-optimized items.
- Search box, drop-downs, lists and tables were restyled to match, and the external overlay uses the same teal accent.
- No behaviour changes: every capability, backup and rollback path is unchanged.

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
