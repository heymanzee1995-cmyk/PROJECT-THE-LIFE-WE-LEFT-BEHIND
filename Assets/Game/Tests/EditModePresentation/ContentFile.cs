using System;
using System.IO;
using Game.Data;

namespace Game.Presentation.Tests
{
    /// <summary>
    /// Loads the real content file. Inside Unity it uses JsonUtility (the serializer the game uses); in the
    /// .NET harness it uses System.Text.Json with fields enabled, which reads the same public fields.
    /// </summary>
    internal static class ContentFile
    {
        public const string RelativePath = "Game/Content/Resources/MergeContent.json";

        public static string ReadText()
        {
#if UNITY_5_3_OR_NEWER
            string path = Path.Combine(UnityEngine.Application.dataPath, RelativePath);
#else
            string path = FindFromRepositoryRoot(Path.Combine("Assets", RelativePath));
#endif
            return File.ReadAllText(path);
        }

        public static MergeContentData Parse(string json)
        {
#if UNITY_5_3_OR_NEWER
            return UnityEngine.JsonUtility.FromJson<MergeContentData>(json);
#else
            return System.Text.Json.JsonSerializer.Deserialize<MergeContentData>(json,
                new System.Text.Json.JsonSerializerOptions { IncludeFields = true });
#endif
        }

        public static MergeContent LoadValid()
        {
            if (!MergeContentMapper.TryBuild(Parse(ReadText()), out MergeContent content, out var errors))
            {
                throw new InvalidOperationException("Content invalid:\n" + string.Join("\n", errors));
            }

            return content;
        }

#if !UNITY_5_3_OR_NEWER
        private static string FindFromRepositoryRoot(string relative)
        {
            string dir = AppContext.BaseDirectory;
            while (dir != null)
            {
                string candidate = Path.Combine(dir, relative);
                if (File.Exists(candidate))
                {
                    return candidate;
                }

                dir = Path.GetDirectoryName(dir);
            }

            throw new FileNotFoundException("Could not find " + relative + " above " + AppContext.BaseDirectory);
        }
#endif
    }
}
