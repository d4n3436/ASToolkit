namespace ASToolkit.Core.Engine;

internal readonly record struct DecodedSoftwareFile(ReadOnlyMemory<byte> Buffer, ReadOnlyMemory<int> FrameOffsets);
