using Kirana.Infrastructure.Security;

namespace Kirana.Tests.Security;

public class BCryptPasswordHasherTests
{
    private readonly BCryptPasswordHasher _hasher = new();

    [Fact]
    public void Hash_ProducesValueDifferentFromPlainText()
    {
        var hash = _hasher.Hash("correct horse battery staple");

        Assert.NotEqual("correct horse battery staple", hash);
    }

    [Fact]
    public void Verify_ReturnsTrue_ForMatchingPlainText()
    {
        var hash = _hasher.Hash("my-secret-pin");

        Assert.True(_hasher.Verify("my-secret-pin", hash));
    }

    [Fact]
    public void Verify_ReturnsFalse_ForWrongPlainText()
    {
        var hash = _hasher.Hash("my-secret-pin");

        Assert.False(_hasher.Verify("wrong-pin", hash));
    }

    [Fact]
    public void Hash_ProducesDifferentSaltedOutput_ForSamePlainTextEachTime()
    {
        var first = _hasher.Hash("1234");
        var second = _hasher.Hash("1234");

        Assert.NotEqual(first, second);
        Assert.True(_hasher.Verify("1234", first));
        Assert.True(_hasher.Verify("1234", second));
    }
}
