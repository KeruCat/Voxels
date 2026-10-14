using System.Runtime.CompilerServices;

// Временный мост до публичного API: сборка Game видит внутренние типы Engine.
// При разделении на проекты эта строка обязательна; со временем внутренние типы, которые нужны игре, станут public.
[assembly: InternalsVisibleTo("Game")]
