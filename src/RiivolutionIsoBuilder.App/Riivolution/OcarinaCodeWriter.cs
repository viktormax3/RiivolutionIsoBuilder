using System.Text;

namespace RiivolutionIsoBuilder.Riivolution;

public static class OcarinaCodeWriter
{
    public static string Write(RiivolutionPatch patch, string gameId, string title)
    {
        var builder = new StringBuilder();
        builder.AppendLine(gameId);
        builder.AppendLine(title);
        builder.AppendLine();

        foreach (var group in patch.MemoryPatches.Where(memory => memory.Value is not null).GroupBy(memory => memory.Tag))
        {
            builder.AppendLine(group.Key);
            foreach (var memory in group)
            {
                foreach (var line in ToOcarinaLines(memory))
                {
                    builder.AppendLine(line);
                }
            }

            builder.AppendLine("E0000000 80008000");
            builder.AppendLine();
        }

        return builder.ToString();
    }

    private static IEnumerable<string> ToOcarinaLines(RiivolutionMemoryPatch patch)
    {
        var address = Convert.ToUInt32(patch.Offset, 16);
        var value = patch.Value ?? "";
        for (var index = 0; index < value.Length; index += 8)
        {
            var chunk = value.Substring(index, Math.Min(8, value.Length - index)).PadRight(8, '0');
            yield return $"{ToWrite32Address(address + (uint)(index / 2))} {chunk}";
        }
    }

    private static string ToWrite32Address(uint address)
    {
        return $"04{address & 0x00FFFFFF:X6}";
    }
}

