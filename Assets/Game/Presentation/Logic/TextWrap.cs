using System.Text;

namespace Game.Presentation
{
    /// <summary>Greedy word wrap for short player-facing messages (placeholder text rendering has no auto-wrap).</summary>
    public static class TextWrap
    {
        public static string Wrap(string text, int maxCharsPerLine)
        {
            if (string.IsNullOrEmpty(text) || maxCharsPerLine < 4 || text.Length <= maxCharsPerLine)
            {
                return text;
            }

            var sb = new StringBuilder(text.Length + 8);
            int lineLength = 0;
            foreach (string word in text.Split(' '))
            {
                if (lineLength > 0 && lineLength + 1 + word.Length > maxCharsPerLine)
                {
                    sb.Append('\n');
                    lineLength = 0;
                }
                else if (lineLength > 0)
                {
                    sb.Append(' ');
                    lineLength++;
                }

                sb.Append(word);
                lineLength += word.Length;
            }

            return sb.ToString();
        }
    }
}
