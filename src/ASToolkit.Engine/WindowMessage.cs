namespace ASToolkit.Engine;

internal readonly record struct WindowMessage(WindowMessageId Id, int WParam, int LParam);
