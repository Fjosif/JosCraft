# Mobilní export — ověřeno 13. 11. 2026

Tato dodávka ověřuje desktopový Godot C# prohlížeč. Mobilní velikost snímku je kontrola rozložení UI, nikoli test telefonu/tabletu.

Oficiální dokumentace Godot 4.7 uvádí podporu Androidu a iOS pro C#, obě zatím experimentální. iOS export vyžaduje macOS; z tohoto Windows prostředí jej nelze ověřit. C# projekt Godot 4 nelze exportovat pro web.

- https://docs.godotengine.org/en/4.7/tutorials/scripting/c_sharp/index.html
- https://docs.godotengine.org/en/4.7/tutorials/export/exporting_for_android.html

Lokální inventura: Godot 4.7.2 Mono, .NET SDK 10.0.401; workload android 36.1.69/10.0.100, ios 26.5.10301/10.0.100. V uživatelském Android SDK existují platformy 34, 35 a 36, platform-tools, build-tools a command-line tools. Ve standardním adresáři Godot export_templates/4.7.2.stable existují Android APK a Windows šablony. Jejich existence sama neprokazuje správné Mono exportní šablony ani úspěšný C# mobilní build. Java není v PATH; JDK cesta pro tento projekt nebyla nastavena. Žádná globální nastavení editoru ani zařízení nebyla změněna.

Před skutečným Android exportem ověřit kompatibilní .NET target a Mono exportní šablony, JDK (dokumentace doporučuje 17), SDK build-tools 35.0.1, platform 35, NDK 28.1.13356709 a CMake 3.10.2.4988404. Potom vytvořit samostatný debug export, zkontrolovat log a až na výslovně určeném testovacím zařízení změřit paměť, FPS, teplotu a dotykové ovládání. Tato etapa neinstaluje nic na rodinná zařízení, nespouští ani neruší emulátory. Fyzický mobilní výkon a mobilní export zatím nejsou ověřené.
