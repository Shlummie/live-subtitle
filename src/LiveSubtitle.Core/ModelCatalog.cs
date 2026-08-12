namespace LiveSubtitle.Core;

public static class ModelCatalog
{
    private const string BaseUri = "https://huggingface.co/ggerganov/whisper.cpp/resolve/main/";

    public static IReadOnlyList<ModelDefinition> Models { get; } =
    [
        new(
            "tiny-en",
            "Fast",
            "Smallest English model for the lowest latency and CPU fallback.",
            "ggml-tiny.en.bin",
            new Uri(BaseUri + "ggml-tiny.en.bin?download=true"),
            77_700_000,
            "921e4cf8686fdd993dcd081a5da5b6c365bfde1162e72b08d75ac75289920b1f"),
        new(
            "base-en",
            "Recommended",
            "The default balance of fast drafts, clear English, and a modest download.",
            "ggml-base.en.bin",
            new Uri(BaseUri + "ggml-base.en.bin?download=true"),
            148_000_000,
            "a03779c86df3323075f5e796cb2ce5029f00ec8869eee3fdfb897afe36c6d002",
            true),
        new(
            "small-en",
            "Accurate",
            "More context and accuracy for clear speech on a capable GPU.",
            "ggml-small.en.bin",
            new Uri(BaseUri + "ggml-small.en.bin?download=true"),
            488_000_000,
            "c6138d6d58ecc8322097e0f987c32f1be8bb0a18532a3f88f734d1bbf9c41e5d"),
        new(
            "turbo-q5",
            "Max GPU",
            "Quantized large-v3-turbo for higher-quality GPU transcription, forced to English.",
            "ggml-large-v3-turbo-q5_0.bin",
            new Uri(BaseUri + "ggml-large-v3-turbo-q5_0.bin?download=true"),
            574_041_195,
            "394221709cd5ad1f40c46e6031ca61bce88931e6e088c188294c6d5a55ffa7e2")
    ];

    public static ModelDefinition Get(string id) =>
        Models.FirstOrDefault(model => string.Equals(model.Id, id, StringComparison.OrdinalIgnoreCase))
        ?? Models.Single(model => model.Recommended);
}
