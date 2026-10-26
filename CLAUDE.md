# AR Vorota — MR-примерка ворот (Meta Quest)

Прототип примерки секционных ворот в смешанной реальности для Quest. Цель — показать заводу/клиентам ворота в реальном проёме.

## Стек
- Unity 6000.3.11, URP, Android (Meta Quest), XR-плагин OpenXR
- Meta XR SDK 85 (Building Blocks: Camera Rig, Passthrough, Real Hands, Interaction SDK)
- Модель ворот: `Assets/Models/lastvorota.glb` (glTFast), анимация открывания — клип `Animation` внутри glb
- package name: `com.vorotakraft.argates` (меняется через Tools → AR Gates → Apply Player Settings;
  у профиля `Assets/Settings/Build Profiles/Meta Quest.asset` свои override player settings — менять в обоих местах)

## Сцена
Единственная сцена в сборке: `Assets/lastvorotanew.unity`.
- `lastvorota` — корень ворот (~200 дочерних мешей из Blender, плохо названы: Cylinder.0xx, Plane.0xx).
  На нём: Animator (`Assets/scripts/GateAnimator.controller`, параметр `GateProgress` → Motion Time),
  `GateController`, `GateMover`.
- `lastvorota/ruchka` — ручка, на ней GrabInteractable + HandGrabInteractable.

## Скрипты (Assets/scripts)
- `GateController.cs` — хват за ручку; вертикальное движение руки/контроллера → прогресс анимации 0..maxProgress.
  Запоминает источник хвата (левый/правый контроллер или рука). `openHandTravelY` в сцене = 2.3. Логи — галочка `debugLogs`.
- `GateMover.cs` — жесты по костям OVRSkeleton: два кулака — перенос + поворот по Y; два указательных (остальные согнуты) —
  масштаб вокруг середины между руками (allowScale). Пока держат ручку (GateController.IsHandleGrabbed) — не работает. Логи — `debugLogs`.
- `GateColorPicker.cs` (на lastvorota) — меню на левой ладони (ладонь к лицу): палитра цвета полотна
  (полотно получает текстурный материал модели, плитка — точный цвет RAL; список Palette в инспекторе)
  + блок «Проём · DISTO» (ширина/высота, выбор поля, Подключить/Сбросить). Нажатие — указательным правой руки.
- `DistoMeasure.cs` (объект DistoMeasure) + `Assets/Plugins/Android/DistoBle.java` — Leica DISTO D110 по BLE:
  сервис 3ab10100-f831-4395-b29d-570977d5bf94, замер = float32 LE в метрах в уведомлениях (как в
  D:\sites\vorotych-project apps/crm/src/zamer/tape.ts). Разрешения BLUETOOTH_SCAN/CONNECT — в
  Assets/Plugins/Android/AndroidManifest.xml. Проверено на шлеме: замеры приходят.
- `GateBuilder.cs` (на lastvorota) — АДАПТИВНЫЕ ворота из `Assets/Models/gate_parts.glb`
  (детали из D:\blender\3d fiber\2025\анимация доска\все цвета\gate_parts.blend; исходник — «перед запеканием8.blend»).
  Полотно = проём + 100 мм по ширине и + 50 мм по высоте; секции 450–600 мм шагом 25; шаблоны Section_1 (низ),
  Section_2/3 (средние, 3 — с ручкой калитки), Section_4 (окна, предпоследняя), Section_5 (верх). Детали
  тянутся/сдвигаются по правилам (широкие — тянутся, крайние — к краю, окна/петли — размер сохраняют).
  Открывание считается кодом: шарниры секций по траектории вертикаль → дуга (liftRadius) → горизонталь,
  Animator прежней модели отключён, старые рендереры скрыты, ручка `ruchka` (с GrabInteractable) переносится на Section_1.
  Направляющие строятся процедурно. Пружин всегда две. Проверка в редакторе (Play Mode): поля debugProgress
  и debugSnapshot (снимки в Logs/gate_preview_*.png). Кнопка «Вписать ворота в проём» в меню на ладони
  собирает ворота по замеру DISTO. Направляющие — процедурный С-профиль (полость к полотну), подвесы на его стенке;
  уплотнители: нижний — «резинка» Section_1 (чёрная), верхний — TopSeal на верхней секции.
  Комплектация: windowsEnabled / wicketEnabled (SetOptions, кнопки «Окна» и «Калитка» в меню на ладони).
  Калитка только при проёме ≥ wicketMinOpeningWidth × wicketMinOpeningHeight (2,0 × 2,0 м); без калитки окна
  ставятся равномерно, сколько помещается.
- `MrLighting.cs` (объект MrLighting) — освещение для MR: ровный «комнатный» ambient (Trilight + SH) и своя кубкарта
  отражений вместо неба Unity; собственный направленный свет MrMainLight (из комнаты сверху, по осям ворот; точечный
  «Directional Light» сцены выключается); тень ворот на пол — FloorShadowCatcher (`Assets/Shaders/ShadowCatcher.shader`,
  высота пола из MRUK); Depth API — EnvironmentDepthManager, мягкое перекрытие, руки тоже перекрывают;
  подстройка яркости/оттенка по камере очков (PassthroughCameraAccess, разрешение horizonos.permission.HEADSET_CAMERA,
  только Quest 3/3S, Horizon OS ≥ 74).
- `GateMaterials.cs` + `Assets/Shaders/GateLitOccluded.shader` — материалы glTFast ворот конвертируются в PBR-шейдер
  с перекрытием Depth API (GateBuilder.Rebuild и GateColorPicker.Apply); нижняя граница шероховатости 0,3,
  резина — неметалл. Depth API требует пакет com.unity.xr.meta-openxr (2.4.1) и OpenXR-фичи Meta Quest Session +
  Occlusion (меню Tools → VorotaKraft → Setup Depth API).
- `OpeningDetector.cs` (объект OpeningDetector) + префаб MRUK (DataSource = Device) — проёмы из разметки пространства
  Quest (DOOR_FRAME, WINDOW_FRAME): голубая рамка с размером, проём под взглядом — белый и пульсирует. Кнопка
  «Вписать в проём» ставит ворота в выбранный проём (GateBuilder.PlaceInOpening: низ — на пол, +X — в комнату),
  размер — по DISTO, если измерен, иначе по разметке. Проверка в редакторе: DataSource = DeviceWithPrefabFallback
  (тестовые комнаты MRUK) и поле debugFitIndex. В манифест нужен USE_SCENE (sceneSupport=Required в OculusProjectConfig).
- `Assets/Editor/ArGatesProjectSetup.cs` — пункт меню для Player Settings.

## Подключение Claude к Unity
- Пакет MCP Unity (CoderGamester, git URL https://github.com/CoderGamester/mcp-unity.git), сервер на порту 8090,
  окно: Tools → MCP Unity → Server Window. «Allow Package Installation» и «Allow Remote Connections» держать выключенными.
- Claude Desktop: в claude_desktop_config.json блок `mcpServers.mcp-unity`
  (node .../Library/PackageCache/com.gamelovers.mcp-unity@<hash>/Server~/build/index.js + env MCP_UNITY_SETTINGS_PATH,
  MCP_UNITY_AUTH_TOKEN_PATH). Кнопка «Configure Claude Desktop» не находит конфиг (MSIX-установка) — вписывать вручную.
  При обновлении пакета меняется <hash> в пути.

## Правила работы
- Git в корне проекта; коммитить после каждой логической правки. Library/Temp/Logs/APK в .gitignore.
- Remote: origin → https://github.com/micronic85/arquestunity (ветка main). Пушить только по просьбе.
  Claude пушит из shell с токеном из C:\Users\User\.git-claude\credentials (подключить папку):
  `git -c credential.helper= -c credential.helper="store --file=$HOME/mnt/.git-claude/credentials" push`.
  С Windows — обычный `git push` (Git Credential Manager).
- После правки скриптов: Assets/Refresh (если удаляли файлы) → recompile → проверить консоль.
- Проверка на устройстве — только в шлеме, Claude этого сделать не может.

## TODO
- Адаптивные ворота: тяга привода (DriveArm) пока скрыта — анимировать; убрать из сцены прежнюю lastvorota.glb
  (сейчас только скрыта, но попадает в сборку); подогнать liftRadius/horizontalExtra под реальные ворота.
- Решить: масштаб для примерки 1:1 (сейчас allowScale = true).
