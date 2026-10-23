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
- Замер проёма лазерной рулеткой Leica DISTO D110 (BLE) → размеры в меню на ладони (ширина/высота, мм).
  Свой Android-плагин (Java) в Assets/Plugins/Android; UUID сервисов сверить по nRF Connect с реального D110.
  Модель ворот НЕ растягивать — только показ размеров.
- Адаптивная (параметрическая) модель ворот: переделать исходник .blend через Blender MCP (группы, имена,
  секции по размеру проёма), потом загружать в проект. Проверить целостность .blend (chkdsk чинил сектора).
- Решить: масштаб для примерки 1:1 (сейчас allowScale = true).
