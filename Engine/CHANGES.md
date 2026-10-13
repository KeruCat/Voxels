# EngineV2: ECS убран, Engine отделён от Game

Один проект, две папки с разными namespace. Разделение на две сборки — следующий шаг,
когда движок заработает (тогда понадобится сделать «внутренние» типы публичными).

## Engine/ — движок (namespace Engine.*)
- App/        запуск (Program, AppCli, AppOptions), часы, Viewport, CrashLog
- Core/       FloatingOrigin (ячейка задаётся игрой), Camera (данные), CameraMath, MathUtil, FramePacer
- Graphics/   GraphicsContext, FrameContext (фрустум, реализует IAabbTest), GpuArena, шейдеры,
              IMeshSink + GpuMeshSink (граница с игрой), GpuRange
- Input/      InputState, InputRouter
- Memory/     MemoryPool, PoolHandle
- Jobs/       CpuPool (пачки структур-задач, Parallel.For)
- UI/         ImGui, DebugUi (мир подключает свою панель через Extra)
- Platform/   TimerResolution

## Game/ — игра (namespace Game.*)
- App/        GameApp: окно, графика, порядок кадра (без систем)
- Gameplay/   Player: позиция (double), скорость, управление камерой и движением
- Render/     VoxelRenderer: пайплайн и инстансинг для вокселей
- Voxels/     Vox (константы 16³), VoxelVertex, генераторы, GreedyMesher
- World/      Chunk (структура), ChunkStore, KeyQueue, ChunkPriority, VoxelWorld (конвейер), WorldSettings

## Порядок кадра
Update: UI → Escape → Player.Update → FloatingOrigin.Follow → VoxelWorld.Update
        (спавн → генерация → соседи готовы → меширование → загрузка в GPU)
Render: BeginFrame → камера (view/proj/фрустум) → VoxelWorld.Cull → BuildDraws → VoxelRenderer.Draw → ImGui → Present

## Проверка
- Чистые части (Memory, Jobs, Core, World, Voxels) скомпилированы .NET 8 без ошибок.
- Части с Diligent / Silk.NET / ImGui не компилировались (нет NuGet в окружении). Ожидаемые правки — по именам и using.

## Что проверить в деле
1. Запуск: видно ли землю, нет ли пустоты/дыр у горизонта.
2. Быстрый полёт: чанки подгружаются ближе к игроку и по взгляду.
3. Выгрузка: память пула и арен не растёт бесконечно (окно Debug).
4. Fade-in: новые чанки проявляются дизеринг-эффектом.
5. Если «наизнанку» — FrontCounterClockwise в VoxelRenderer.

## Известные ограничения
- Пул CPU синхронный (Parallel.For внутри Update).
- Редактирования блоков нет.
