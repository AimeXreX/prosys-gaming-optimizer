using System.Windows;
using System.Windows.Controls;
using System.Text.RegularExpressions;

namespace ProSyS.App;

public sealed class UiLocalization
{
    private readonly Dictionary<DependencyObject, string> _originals = new();
    private static readonly IReadOnlyDictionary<string, string> Persian = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["Dashboard"] = "داشبورد", ["Live system readiness and safe optimization"] = "وضعیت زنده سیستم و بهینه‌سازی امن",
        ["▰   DASHBOARD"] = "▰   داشبورد", ["◉   SYSTEM SCAN"] = "◉   اسکن کامل سیستم", ["✦   RECOMMENDATIONS"] = "✦   مرکز بهینه‌سازی",
        ["⌁   DIAGNOSTICS"] = "⌁   پایش و عیب‌یابی", ["↶   HISTORY & BACKUPS"] = "↶   تاریخچه و بازیابی", ["⚙   SETTINGS"] = "⚙   تنظیمات",
        ["◫   BENCHMARK & A/B"] = "◫   بنچمارک و مقایسه", ["🎮   GAME PROFILES"] = "🎮   پروفایل بازی‌ها", ["▣   OVERLAY"] = "▣   نمایشگر زنده",
        ["PRESENTMON BENCHMARK"] = "بنچمارک واقعی PRESENTMON", ["Capture real frame times, FPS lows and variance without injecting into the game."] = "ثبت واقعی زمان فریم، افت‌های FPS و نوسان بدون تزریق به بازی.",
        ["CAPTURE SETTINGS"] = "تنظیمات ثبت", ["Game process name"] = "نام پردازش بازی", ["Duration (seconds)"] = "مدت (ثانیه)", ["Checking PresentMon trust…"] = "در حال بررسی اعتبار PresentMon…", ["START CAPTURE"] = "شروع ثبت",
        ["LATEST RESULT"] = "آخرین نتیجه", ["No benchmark has been captured yet."] = "هنوز بنچمارکی ثبت نشده است.", ["COMPARE LAST TWO RUNS"] = "مقایسه دو اجرای آخر", ["BENCHMARK HISTORY"] = "تاریخچه بنچمارک",
        ["GAME PROFILES"] = "پروفایل بازی‌ها", ["Save per-game optimization choices and restore temporary changes when the game exits."] = "تنظیمات مخصوص هر بازی را ذخیره و تغییرات موقت را پس از خروج بازی خودکار بازگردانی کنید.",
        ["PROFILE EDITOR"] = "ویرایش پروفایل", ["Profile name"] = "نام پروفایل", ["Game process"] = "پردازش بازی", ["Executable path (optional)"] = "مسیر فایل اجرایی (اختیاری)",
        ["Safe"] = "امن", ["Balanced"] = "متعادل", ["Competitive"] = "رقابتی", ["Restore temporary changes when game exits"] = "بازگردانی تغییرات موقت پس از خروج بازی", ["Show safe external overlay"] = "نمایش Overlay امن و خارجی",
        ["SAVE PROFILE"] = "ذخیره پروفایل", ["LAUNCH"] = "اجرا", ["SAVED PROFILES"] = "پروفایل‌های ذخیره‌شده",
        ["SAFE PERFORMANCE OVERLAY"] = "نمایشگر زنده و امن کارایی", ["A click-through external window; no DLL injection and no game memory access."] = "پنجره‌ای خارجی و کلیک‌گذر؛ بدون تزریق DLL و بدون دسترسی به حافظه بازی.",
        ["OVERLAY STATUS"] = "وضعیت نمایشگر", ["Stopped"] = "متوقف", ["Displays CPU, memory and network throughput using documented Windows counters."] = "مصرف پردازنده، حافظه و سرعت شبکه را با شمارنده‌های مستند ویندوز نمایش می‌دهد.", ["SHOW OVERLAY"] = "نمایش", ["HIDE OVERLAY"] = "پنهان‌کردن",
        ["PROTECTION"] = "محافظت فعال", ["  ✓  Backup before changes"] = "  ✓  پشتیبان‌گیری پیش از تغییر", ["  ✓  Verify after apply"] = "  ✓  تأیید نتیجه پس از اعمال",
        ["  ✓  Telemetry disabled"] = "  ✓  تله‌متری خاموش", ["● LOCAL MODE"] = "● حالت کاملاً محلی", ["No cloud • No account"] = "بدون ابر • بدون حساب کاربری",
        ["NOT SCANNED"] = "اسکن نشده", ["READY WHEN YOU ARE"] = "آماده برای بررسی سیستم", ["Run a scan to build a hardware-aware optimization plan."] = "برای ساخت برنامه بهینه‌سازی سازگار با سخت‌افزار، اسکن را اجرا کنید.",
        ["SCAN THIS PC"] = "اسکن این رایانه", ["EXPORT REPORT"] = "خروجی گزارش", ["READINESS"] = "آمادگی گیمینگ", ["WINDOWS"] = "ویندوز", ["CPU"] = "پردازنده",
        ["MEMORY"] = "حافظه", ["OPTIMIZATIONS"] = "گزینه‌های موجود", ["SYSTEM OVERVIEW"] = "نمای کلی سیستم", ["Never scanned"] = "هنوز اسکن نشده",
        ["QUICK OPTIMIZE"] = "بهینه‌سازی سریع", ["Only selected, compatible and reversible changes are applied."] = "فقط تغییرات انتخاب‌شده، سازگار و قابل‌بازگشت اعمال می‌شوند.",
        ["REVIEW PLAN"] = "بررسی برنامه", ["APPLY SELECTED"] = "اعمال گزینه‌های انتخاب‌شده", ["No immutable plan yet"] = "هنوز برنامه نهایی ساخته نشده",
        ["Detected hardware & software"] = "سخت‌افزار و نرم‌افزار شناسایی‌شده", ["SCAN AGAIN"] = "اسکن مجدد", ["HARDWARE & WINDOWS"] = "سخت‌افزار و ویندوز",
        ["INSTALLED GAMES (STEAM)"] = "بازی‌های نصب‌شده (استیم)", ["NETWORK ADAPTERS"] = "کارت‌های شبکه", ["STARTUP REGISTRATIONS"] = "برنامه‌های شروع خودکار",
        ["Name"] = "نام", ["Source"] = "منبع", ["Executable"] = "فایل اجرایی", ["All recommendations"] = "همه گزینه‌ها", ["Selected"] = "انتخاب‌شده",
        ["Available"] = "قابل اعمال", ["Already optimized"] = "از قبل بهینه", ["Search recommendations"] = "جست‌وجوی گزینه‌ها", ["OPTIMIZATION DETAILS"] = "جزئیات بهینه‌سازی",
        ["Select a recommendation"] = "یک گزینه را انتخاب کنید", ["Technical and safety details will appear here."] = "توضیحات فنی، شواهد و روش بازیابی اینجا نمایش داده می‌شود.",
        ["Live diagnostics snapshot"] = "نمای زنده عیب‌یابی سیستم", ["REFRESH"] = "تازه‌سازی", ["AVAILABLE MEMORY"] = "حافظه آزاد", ["RUNNING SERVICES"] = "سرویس‌های در حال اجرا",
        ["NETWORK UP"] = "تأخیر دروازه شبکه", ["ANTI-CHEAT"] = "ضدتقلب", ["TOP MEMORY CONSUMERS"] = "پردازش‌های پرمصرف", ["Process"] = "پردازش", ["Memory"] = "حافظه",
        ["CPU time"] = "زمان پردازنده", ["Class"] = "دسته", ["History & recovery"] = "تاریخچه و بازیابی", ["Every change session is journaled locally."] = "تمام تغییرات به‌صورت محلی ثبت و قابل بازیابی‌اند.",
        ["SELECTED SESSION"] = "نشست انتخاب‌شده", ["Choose a session to inspect its recovery state."] = "یک نشست را برای مشاهده وضعیت بازیابی انتخاب کنید.", ["RESTORE ORIGINAL STATE"] = "بازگردانی وضعیت اصلی",
        ["Settings"] = "تنظیمات", ["Local preferences and transparency controls"] = "ترجیحات محلی و کنترل‌های شفافیت", ["LANGUAGE & DIRECTION"] = "زبان و جهت رابط",
        ["Switch the interface between English/LTR and Persian/RTL."] = "رابط را بین فارسی راست‌به‌چپ و انگلیسی چپ‌به‌راست تغییر دهید.", ["SWITCH ENGLISH / فارسی"] = "تغییر زبان فارسی / English",
        ["LOCAL DATA"] = "داده‌های محلی", ["PRIVACY"] = "حریم خصوصی", ["Switch language and direction"] = "تغییر زبان و جهت رابط",
        ["Telemetry: OFF\nCloud account: NOT REQUIRED\nHardware identifiers: NOT EXPORTED\nOptimization and backup data remain on this PC."] = "تله‌متری: خاموش\nحساب ابری: لازم نیست\nشناسه‌های سخت‌افزاری: صادر نمی‌شوند\nداده‌های بهینه‌سازی و پشتیبان روی همین رایانه می‌مانند.",
        ["SYSTEM RECOVERY"] = "بازیابی سیستم", ["Create a Windows restore point through the short-lived, allowlisted administrator helper."] = "یک نقطه بازیابی ویندوز با ابزار مدیریتی کوتاه‌عمر و محدودشده ایجاد کنید.", ["CREATE RESTORE POINT"] = "ساخت نقطه بازیابی",

        ["Disable background game capture"] = "غیرفعال‌کردن ضبط پس‌زمینه بازی", ["Disable historical capture"] = "غیرفعال‌کردن ضبط لحظات گذشته", ["Disable Game DVR"] = "غیرفعال‌کردن Game DVR",
        ["Enable automatic Game Mode"] = "فعال‌کردن خودکار حالت بازی", ["Allow Game Mode"] = "اجازه به حالت بازی ویندوز", ["Hide Game Bar startup panel"] = "پنهان‌کردن پنل آغاز Game Bar",
        ["Hide Game Mode notifications"] = "پنهان‌کردن اعلان‌های حالت بازی", ["Disable capture audio"] = "غیرفعال‌کردن صدای ضبط", ["Disable capture microphone"] = "غیرفعال‌کردن میکروفن ضبط", ["Hide cursor in captures"] = "پنهان‌کردن نشانگر در ضبط",
        ["Disable mouse acceleration"] = "غیرفعال‌کردن شتاب ماوس", ["Remove mouse acceleration threshold 1"] = "حذف آستانه اول شتاب ماوس", ["Remove mouse acceleration threshold 2"] = "حذف آستانه دوم شتاب ماوس", ["Prefer performance visual effects"] = "اولویت‌دادن به کارایی جلوه‌های ویندوز",
        ["Stops Windows background capture for the current user."] = "ضبط پس‌زمینه ویندوز را برای کاربر فعلی متوقف می‌کند.", ["Stops retrospective gameplay recording for the current user."] = "ذخیره دائمی لحظات گذشته بازی را متوقف می‌کند.",
        ["Disables Game DVR capture in the current user's game configuration."] = "ضبط Game DVR را در تنظیمات کاربر فعلی غیرفعال می‌کند.", ["Lets Windows recognize games and prioritize the gaming workload."] = "به ویندوز اجازه می‌دهد بازی را شناسایی و بار گیمینگ را اولویت‌بندی کند.",
        ["Enables the supported Windows Game Mode policy for the current user."] = "قابلیت رسمی Game Mode را برای کاربر فعلی فعال می‌کند.", ["Prevents the Game Bar welcome panel from opening at game startup."] = "از بازشدن پنل خوش‌آمد Game Bar هنگام اجرای بازی جلوگیری می‌کند.",
        ["Stops informational Game Mode popups while a game is starting."] = "اعلان‌های مزاحم Game Mode هنگام اجرای بازی را متوقف می‌کند.", ["Prevents Game DVR from including system audio in new captures."] = "از ثبت صدای سیستم در ضبط‌های جدید جلوگیری می‌کند.",
        ["Prevents Game DVR from including microphone audio in new captures."] = "از ثبت صدای میکروفن در ضبط‌های جدید جلوگیری می‌کند.", ["Prevents Game DVR from recording the mouse pointer."] = "از ثبت نشانگر ماوس در ضبط‌ها جلوگیری می‌کند.",
        ["Turns off Windows pointer acceleration for consistent physical-to-pointer movement."] = "شتاب نشانگر ویندوز را برای حرکت یکنواخت و قابل‌پیش‌بینی غیرفعال می‌کند.",
        ["Clears the first legacy Windows pointer acceleration threshold."] = "آستانه اول شتاب قدیمی نشانگر ویندوز را حذف می‌کند.", ["Clears the second legacy Windows pointer acceleration threshold."] = "آستانه دوم شتاب قدیمی نشانگر ویندوز را حذف می‌کند.",
        ["Selects the Windows performance-oriented visual-effects preset for the current user."] = "پروفایل جلوه‌های بصری مبتنی بر کارایی را برای کاربر فعلی انتخاب می‌کند.",
        ["Background recording can consume encoding and storage resources while gaming."] = "ضبط پس‌زمینه ممکن است هنگام بازی منابع پردازش و ذخیره‌سازی مصرف کند.",
        ["Retrospective capture keeps a rolling recording buffer and may add overhead."] = "ضبط لحظات گذشته یک بافر دائمی نگه می‌دارد و ممکن است سربار ایجاد کند.",
        ["Disabling unused recording avoids capture-related work; it does not guarantee higher FPS."] = "غیرفعال‌کردن ضبط بلااستفاده سربار مربوط به Capture را حذف می‌کند؛ افزایش FPS تضمین نمی‌شود.",
        ["Windows Game Mode is designed to prioritize the game and reduce disruptive background activity."] = "Game Mode برای اولویت‌دادن به بازی و کاهش فعالیت مزاحم پس‌زمینه طراحی شده است.",
        ["This keeps the official Game Mode feature available without weakening security."] = "قابلیت رسمی Game Mode را بدون کاهش امنیت در دسترس نگه می‌دارد.",
        ["Reduces an unnecessary UI interruption; performance benefit is negligible."] = "مزاحمت غیرضروری رابط را کاهش می‌دهد؛ اثر کارایی ناچیز است.",
        ["Avoids an on-screen interruption; this is a convenience option, not an FPS boost."] = "از نمایش اعلان مزاحم جلوگیری می‌کند؛ این گزینه افزایش‌دهنده FPS نیست.",
        ["Useful only when Windows capture is not used; no direct FPS improvement is claimed."] = "فقط وقتی ضبط ویندوز استفاده نمی‌شود مفید است و ادعای افزایش مستقیم FPS ندارد.",
        ["A privacy-oriented recording preference; it is not presented as a performance tweak."] = "یک ترجیح حریم خصوصی برای ضبط است و به‌عنوان بهینه‌سازی کارایی معرفی نمی‌شود.",
        ["A recording preference with negligible performance effect."] = "یک ترجیح ضبط با اثر کارایی ناچیز است.",
        ["Competitive players may prefer predictable pointer movement; this is a personal input preference."] = "بازیکنان رقابتی ممکن است حرکت قابل‌پیش‌بینی ماوس را ترجیح دهند؛ این یک انتخاب شخصی است.",
        ["This supports the optional no-acceleration input profile and is not an FPS tweak."] = "از پروفایل اختیاری بدون شتاب ماوس پشتیبانی می‌کند و یک ترفند FPS نیست.",
        ["Reducing desktop animations may lower UI overhead, but it does not guarantee better in-game performance."] = "کاهش انیمیشن‌های دسکتاپ ممکن است سربار رابط را کم کند، اما بهبود بازی را تضمین نمی‌کند.",
        ["Already in the recommended state."] = "این گزینه از قبل در وضعیت پیشنهادی است.", ["GenerallySupportedBehavior"] = "رفتار عمومی پشتیبانی‌شده", ["Low"] = "کم", ["Negligible"] = "ناچیز",
        ["Gaming & Capture"] = "بازی و ضبط", ["Input"] = "ورودی و ماوس", ["Windows Shell"] = "پوسته ویندوز",
        ["Desktop Responsiveness"] = "پاسخ‌دهی دسکتاپ", ["Accessibility & Input"] = "دسترس‌پذیری و ورودی",
        ["Personalization"] = "شخصی‌سازی", ["Desktop Composition"] = "ترکیب‌بندی دسکتاپ",
        ["Search & Background"] = "جست‌وجو و پس‌زمینه", ["Background Content"] = "محتوای پس‌زمینه",
        ["An allowlisted Windows 11 user preference. Measure the result and retain it only when it improves the intended workflow."] = "یک تنظیم مجاز و محدودشده برای کاربر ویندوز ۱۱ است. نتیجه را اندازه‌گیری کنید و فقط در صورت بهبود تجربه موردنظر آن را نگه دارید.",
        ["Restore the exact original registry value and type, or remove it when it did not exist."] = "مقدار و نوع دقیق قبلی رجیستری را بازمی‌گرداند؛ اگر قبلاً وجود نداشته باشد، مقدار ایجادشده حذف می‌شود."
        ,["Game Panel Startup Tip Index"] = "شاخص راهنمای آغاز پنل بازی", ["Mouse Threshold1"] = "آستانه اول شتاب ماوس", ["Mouse Threshold2"] = "آستانه دوم شتاب ماوس",
        ["Active Window Tracking"] = "ردیابی پنجره فعال", ["Beep"] = "صدای هشدار", ["Icons Only"] = "نمایش فقط آیکن‌ها", ["Hidden"] = "نمایش موارد مخفی",
        ["Separate Process"] = "اجرای جداگانه پردازش اکسپلورر", ["Launch To"] = "صفحه آغاز فایل اکسپلورر", ["Nav Pane Expand To Current Folder"] = "بازکردن مسیر جاری در پنل ناوبری",
        ["Auto Check Select"] = "انتخاب خودکار با کادر علامت", ["MMTaskbar Glom Level"] = "گروه‌بندی نوار وظیفه نمایشگر دوم", ["Start Track Docs"] = "ثبت اسناد اخیر در منوی شروع",
        ["Start Track Progs"] = "ثبت برنامه‌های اخیر در منوی شروع", ["Start Iris Recommendations"] = "پیشنهادهای بخش شروع", ["Persist Browsers"] = "حفظ پنجره‌های مرورگر",
        ["Reindexed Profile"] = "وضعیت بازفهرست‌گذاری پروفایل", ["Auto End Tasks"] = "بستن خودکار برنامه‌ها", ["Low Level Hooks Timeout"] = "مهلت Hookهای سطح پایین",
        ["Drag Full Windows"] = "نمایش کامل پنجره هنگام جابه‌جایی", ["Smooth Scroll"] = "پیمایش نرم", ["Click Lock Time"] = "زمان قفل کلیک", ["Caret Timeout"] = "مهلت نشانگر نوشتار",
        ["JPEGImport Quality"] = "کیفیت واردکردن JPEG", ["Pattern"] = "الگوی پس‌زمینه", ["Tile Wallpaper"] = "چیدمان کاشی تصویر زمینه", ["Wallpaper Style"] = "سبک تصویر زمینه",
        ["Last Bounce Key Setting"] = "آخرین تنظیم جلوگیری از تکرار کلید", ["Last Valid Repeat"] = "آخرین مقدار معتبر تکرار کلید", ["Last Valid Wait"] = "آخرین زمان انتظار معتبر",
        ["Tri State"] = "حالت سه‌گانه کلیدهای چسبان", ["Two Keys Off"] = "خاموش‌شدن با فشردن دو کلید", ["Use Ctrl Alt"] = "استفاده از Ctrl و Alt",
        ["Always Hibernate Thumbnails"] = "نگه‌داری همیشگی تصاویر بندانگشتی", ["Accent Color Inactive"] = "رنگ تأکیدی پنجره غیرفعال", ["Composition"] = "ترکیب‌بندی پنجره‌ها",
        ["Colorization Opaque Blend"] = "ترکیب مات رنگ پنجره", ["Cortana Consent"] = "مجوز کورتانا", ["Remediation Required"] = "نیاز به اصلاح محتوای پیشنهادی"
    };

    private static readonly (string English, string Persian)[] GeneratedTerms =
    {
        ("Historical Capture", "ضبط لحظات گذشته"), ("Game Mode", "حالت بازی"), ("Game DVR", "ضبط بازی"),
        ("Game Bar", "نوار بازی"), ("VKMToggle", "میان‌بر صفحه‌کلید/ماوس"), ("VKToggle", "میان‌بر صفحه‌کلید"), ("Toggle", "تغییر وضعیت"),
        ("Mouse Keys", "کلیدهای ماوس"), ("Auto Repeat", "تکرار خودکار"), ("Double Click", "دوبارکلیک"),
        ("Maximum Speed", "حداکثر سرعت"), ("Time To Maximum Speed", "زمان رسیدن به حداکثر سرعت"),
        ("Content Delivery", "ارائه محتوا"), ("Pre Installed Apps", "برنامه‌های ازپیش‌نصب‌شده"),
        ("System Pane Suggestions", "پیشنهادهای پنل سیستم"), ("Rotating Lock Screen", "صفحه قفل چرخشی"),
        ("App Capture", "ضبط برنامه"), ("Audio Capture", "ضبط صدای سیستم"), ("Microphone Capture", "ضبط میکروفن"),
        ("Cursor Capture", "ضبط نشانگر"), ("Echo Cancellation", "حذف پژواک"), ("Video Encoding", "رمزگذاری ویدئو"),
        ("Frame Rate", "نرخ فریم"), ("Bitrate Mode", "حالت نرخ بیت"), ("Resolution Mode", "حالت وضوح"),
        ("Startup Panel", "پنل آغاز"), ("Notifications", "اعلان‌ها"), ("Performance Widget", "ابزارک کارایی"),
        ("Capture Widget", "ابزارک ضبط"), ("Audio Widget", "ابزارک صدا"), ("Gallery Widget", "ابزارک گالری"),
        ("Resources Widget", "ابزارک منابع"), ("Social Widget", "ابزارک اجتماعی"), ("Xbox Chat Widget", "ابزارک گفت‌وگوی Xbox"),
        ("Widget Transparency", "شفافیت ابزارک"), ("Mouse Sensitivity", "حساسیت ماوس"), ("Mouse Hover Time", "زمان مکث ماوس"),
        ("Mouse Threshold", "آستانه شتاب ماوس"), ("Mouse Speed", "سرعت ماوس"), ("Mouse Trails", "رد ماوس"),
        ("Swap Mouse Buttons", "جابه‌جایی دکمه‌های ماوس"), ("Snap To Default Button", "پرش به دکمه پیش‌فرض"),
        ("Taskbar", "نوار وظیفه"), ("Listview", "نمای فهرست"), ("File Ext", "پسوند فایل"),
        ("Super Hidden", "فایل‌های سیستمی مخفی"), ("Preview Desktop", "پیش‌نمایش دسکتاپ"),
        ("Snap Assist Flyout", "منوی چیدمان پنجره‌ها"), ("Snap Bar", "نوار چیدمان پنجره‌ها"),
        ("Menu Show Delay", "تأخیر نمایش منو"), ("Hung App Timeout", "مهلت برنامه بدون پاسخ"),
        ("Wait To Kill App Timeout", "مهلت بستن برنامه"), ("Foreground Lock Timeout", "مهلت قفل پنجره فعال"),
        ("Foreground Flash Count", "تعداد چشمک پنجره"), ("Font Smoothing", "هموارسازی فونت"),
        ("Wheel Scroll Lines", "خطوط پیمایش چرخ ماوس"), ("Wheel Scroll Chars", "نویسه‌های پیمایش افقی"),
        ("Cursor Blink Rate", "سرعت چشمک مکان‌نما"), ("Screen Save Active", "فعال‌بودن محافظ صفحه"),
        ("Search History", "تاریخچه جست‌وجو"), ("Device History", "تاریخچه دستگاه"), ("Safe Search Mode", "حالت جست‌وجوی امن"),
        ("Transparency", "شفافیت"), ("Light Theme", "پوسته روشن"), ("Color Prevalence", "نمایش رنگ تأکیدی"),
        ("Aero Peek", "پیش‌نمایش Aero"), ("Window Colorization", "رنگ‌آمیزی پنجره"),
        ("Hot Key", "میان‌بر صفحه‌کلید"), ("Audible Feedback", "بازخورد صوتی"), ("Bounce Time", "زمان جلوگیری از تکرار کلید"),
        ("Delay Before Acceptance", "تأخیر پذیرش کلید"), ("Recommended", "پیشنهادی"),
        ("Enabled", "فعال"), ("Allowed", "مجاز"), ("Enable", "فعال‌سازی"), ("Disable", "غیرفعال‌سازی"),
        ("Show", "نمایش"), ("Hide", "پنهان‌سازی"), ("Remember", "به‌خاطرسپاری"), ("Maximum", "حداکثر"),
        ("Delay", "تأخیر"), ("Speed", "سرعت"), ("Width", "عرض"), ("Height", "ارتفاع"),
        ("Flags", "پرچم‌های تنظیم"), ("On", "روشن"), ("Mode", "حالت"), ("Behavior", "رفتار"),
        ("Notifications", "اعلان‌ها"), ("History", "تاریخچه"), ("Suggestions", "پیشنهادها")
    };

    public void Apply(DependencyObject root, bool usePersian)
    {
        foreach (var element in Walk(root))
        {
            if (element is TextBlock text) ApplyValue(text, text.Text, value => text.Text = value, usePersian);
            if (element is ContentControl content && content.Content is string value) ApplyValue(content, value, translated => content.Content = translated, usePersian);
            if (element is FrameworkElement framework && framework.ToolTip is string tip) ApplyToolTip(framework, tip, usePersian);
        }
    }

    public static string Translate(string value, bool usePersian)
    {
        if (!usePersian) return value;
        if (Persian.TryGetValue(value, out var translated)) return translated;
        const string prefix = "Configure the current-user ";
        const string suffix = " preference.";
        if (value.StartsWith(prefix, StringComparison.Ordinal) && value.EndsWith(suffix, StringComparison.Ordinal))
            return $"تنظیم ترجیح «{TranslateGenerated(value[prefix.Length..^suffix.Length])}» برای کاربر فعلی.";
        return TranslateGenerated(value);
    }

    private static string TranslateGenerated(string value)
    {
        var translated = value;
        foreach (var (english, persian) in GeneratedTerms.OrderByDescending(x => x.English.Length))
            translated = Regex.Replace(translated, $@"\b{Regex.Escape(english)}\b", persian, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        return translated;
    }

    private void ApplyValue(DependencyObject owner, string current, Action<string> setter, bool usePersian)
    {
        if (!_originals.TryGetValue(owner, out var original)) { original = current; _originals[owner] = original; }
        setter(Translate(original, usePersian));
    }

    private void ApplyToolTip(FrameworkElement owner, string current, bool usePersian)
    {
        if (!_originals.TryGetValue(owner, out var original)) { original = current; _originals[owner] = original; }
        owner.ToolTip = Translate(original, usePersian);
    }

    private static IEnumerable<DependencyObject> Walk(DependencyObject root)
    {
        yield return root;
        foreach (var child in LogicalTreeHelper.GetChildren(root))
            if (child is DependencyObject dependency)
                foreach (var descendant in Walk(dependency)) yield return descendant;
    }
}
