# Miracle Arabic

Miracle Arabic is a small Windows app for Arabic players.

Some Unity games (and a few other programs) can't display Arabic properly. They show it broken, or they show the letters in the wrong order. The workaround that actually works is to hand the game the text in its "visual" form, i.e. the way it is supposed to look on screen, and then the game draws it correctly.

So the idea is simple. You write normally in Arabic, the app converts it to that visual form, and sends it to the game. It also does the opposite: when you copy Arabic out of the game and it looks broken, CORRECT puts it back into normal readable Arabic in the text box.

## What it does

- **Arabic output**: type Arabic and get the shaped/reversed form that Unity games display correctly.
- **EASY mode**: sends what you wrote straight to the game window, so you don't have to copy and paste by hand.
- **CORRECT**: fixes Arabic that you copy from the game. If translation is on, it can also show you the copied text translated.
- **TRANSLATE**: translates what you type, or what you copy, using Google or DeepL, in both directions.
- **Macros**: small hotkey combinations that paste ready-made text.
- **Arabic interface**: the settings and the whole UI work in Arabic.

## Supported games

- **Meredian 3DXChat**: the main one. This is the build where the "chat via game engine" route works, with direct send.
- **Match3D Reborn**: a fork of 3DXChat. It is supported, but only through the keyboard route for now; direct send doesn't work there yet.
- The official 3DXChat build is not supported at the moment. If it becomes possible, I'll add it.

## What is not in this repository

I want to be clear about this part instead of leaving people guessing.

- **The chat bridge** (the small DLL that gets loaded into the game) is not here. That code is very specific to the game build, so I didn't publish it.
- **The injector project** is something separate that has nothing to do with Miracle Arabic. It is not here.
- Anything related to the other game that the bridge talks to is left out as well.

Because of that, the "chat via game engine" option in this source drop will always say the bridge is unavailable, and the app will fall back to the keyboard route. The keyboard route (paste and Enter into the game chat) works fine on its own.

## About the injection

Yes, the tool does load a small DLL into the game. In plain words:

1. the app opens the game process,
2. it loads the bridge DLL inside it,
3. the DLL talks to the game's own chat object and calls the game's own send function.

That is what makes Arabic arrive exactly as written. It is also why some antivirus programs may warn about it or block it, because loading a DLL into another process looks suspicious to them. If that makes you uncomfortable, you can stay on the keyboard route only (Settings, Easy Mode sending, Keyboard keys). I understand people who would rather not run it, and that is exactly why the bridge code is not published here.

## Requirements

- Windows 10 or 11
- .NET 8 SDK
- Visual Studio 2022, or just the .NET CLI

## Build

```
dotnet build "Miracle Arabic.csproj" -c Release
```

The exe ends up in `bin\Release\net8.0-windows\`.

If you want a single-file exe:

```
dotnet publish "Miracle Arabic.csproj" -c Release -r win-x64 --self-contained false -p:PublishSingleFile=true
```

## Notes

- Settings are saved in `%APPDATA%\Miracle Arabic Fix\config.json`.
- The DeepL key is stored encrypted with Windows DPAPI, so the file never holds it in plain text.
- The Google translation uses the public web endpoint, so it can stop working if Google changes it.
- The update check is disabled in this build on purpose (there is no release feed attached). If you want it, set `UpdateUrl` in `MainWindow.xaml.cs` to your own `version.json`.

## License

MIT. See `LICENSE`.

---

## بالعربية

«ميراكل عربي» أداة صغيرة لنظام ويندوز، صنعتها للاعبين العرب.

بعض ألعاب يونيتي (وقليل من البرامج الأخرى) لا تعرف عرض النصّ العربي كما ينبغي، فتظهره مقطّعاً أو مقلوب الحروف. والحلّ الذي يعمل فعلاً هو تمرير النصّ إلى اللعبة بشكله البصري، أي بالترتيب الذي ينبغي أن يظهر به على الشاشة، فتعرضه اللعبة عرضاً صحيحاً.

والفكرة بسيطة: تكتب بالعربية بشكل طبيعي، فتحوّله الأداة إلى الشكل الذي تفهمه اللعبة وترسله. وهي تفعل العكس أيضاً؛ فعند نسخ نصّ عربي من اللعبة يظهر مقطّعاً، تعيده ميزة CORRECT إلى نصّ عربي سليم في مربّع الكتابة.

المزايا باختصار: إرسال النصّ العربي إلى اللعبة، ووضع EASY لتجنّب النسخ واللصق اليدوي، وميزة CORRECT لإصلاح النصّ المنسوخ وعرضه مترجَماً، والترجمة في الاتجاهين عبر جوجل أو DeepL، واختصارات الماكرو، وواجهة عربية.

الألعاب المدعومة: Meredian 3DXChat (وفيها إرسال مباشر)، و Match3D Reborn (نسخة متفرّعة من 3DXChat، وهي مدعومة حالياً عبر لوحة المفاتيح فقط). أمّا النسخة الرسمية من 3DXChat فغير مدعومة حتى الآن، وإن أمكن ذلك فسأضيفها.

**ما ليس موجوداً في هذا المستودع:** جسر الدردشة (الملفّ DLL الذي يُحمَّل داخل اللعبة) غير موجود هنا، لأنه خاصّ ببنية اللعبة. ومشروع «الإنجكتور» شيء منفصل لا علاقة له بالأداة. ولذلك فإنّ خيار «الدردشة عبر محرّك اللعبة» في نسخة المصدر هذه سيُفيد بأنّ الجسر غير متاح، وستعمل الأداة عبر لوحة المفاتيح.

والأداة تحمّل فعلاً ملفّ DLL داخل اللعبة، وهذا هو سبب أنّ بعض برامج الحماية قد تعدّه مشبوهاً. فإن لم يكن ذلك مريحاً لك، فاستخدم مسار لوحة المفاتيح وحده. ولهذا السبب لا أنشر شيفرة الجسر هنا.