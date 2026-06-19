namespace FtrIO.OneTwo;

internal enum ToggleSource { Attribute, AsyncAttribute, ManualCall, AsyncManualCall }

internal record ToggleEntry(
    string ToggleKey,
    string MethodName,
    string File,
    int Line,
    ToggleSource Source,
    bool? State   // null = key not found in appsettings
);
