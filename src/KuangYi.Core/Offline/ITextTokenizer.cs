namespace KuangYi.Core.Offline;

public interface ITextTokenizer
{
    int UnknownId { get; }

    int EndOfSentenceId { get; }

    int PadId { get; }

    List<long> Encode(string text, int appendEos = -1);

    string Decode(IEnumerable<long> ids, IReadOnlySet<int>? skipIds = null);

    int IdOf(string piece);
}
