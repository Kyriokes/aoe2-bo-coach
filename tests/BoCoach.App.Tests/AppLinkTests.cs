using BoCoach.App.Web;

namespace BoCoach.App.Tests;

/// <summary>Los enlaces bocoach:// que manda la web: conectar la cuenta y sumar una BO (parte 4).</summary>
public class AppLinkTests
{
    private const string Token = "bocoach_0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef";

    [Fact]
    public void Conectar_con_un_token_valido()
    {
        Assert.Equal(new AppLink.Connect(Token), AppLink.Parse($"bocoach://conectar/{Token}"));
        Assert.Equal(new AppLink.Connect(Token), AppLink.Parse($" BOCOACH://conectar/{Token}/ "));
    }

    [Theory]
    [InlineData("bocoach://conectar/")]
    [InlineData("bocoach://conectar/bocoach_corto")]
    [InlineData("bocoach://conectar/bocoach_0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdzz")]
    [InlineData("bocoach://conectar/bocoach_0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef\" --screenshot x")]
    public void Un_token_que_no_sirve_no_es_un_enlace(string text)
    {
        Assert.Null(AppLink.Parse(text));
    }

    [Theory]
    [InlineData("bocoach://import/k7m2xq")]
    [InlineData("https://aoe2-bo-coach.vercel.app/bo/k7m2xq")]
    [InlineData("k7m2xq")]
    public void Sumar_una_BO_por_su_codigo(string text)
    {
        Assert.Equal(new AppLink.Import("k7m2xq"), AppLink.Parse(text));
    }

    [Theory]
    [InlineData("")]
    [InlineData("activate")]
    [InlineData("bocoach://otra/cosa")]
    public void Lo_demas_no_es_un_enlace(string text)
    {
        Assert.Null(AppLink.Parse(text));
    }

    // AP-08: lo que se pega en "Pegar código" puede ser el token, solo o en su enlace.
    [Fact]
    public void Encuentra_el_token_solo_o_en_su_enlace()
    {
        Assert.Equal(Token, AppLink.TokenIn(Token));
        Assert.Equal(Token, AppLink.TokenIn($"  {Token.ToUpperInvariant()} "));
        Assert.Equal(Token, AppLink.TokenIn($"bocoach://conectar/{Token}/"));
        Assert.Null(AppLink.TokenIn("bocoach_corto"));
        Assert.Null(AppLink.TokenIn("k7m2xq"));
        Assert.Null(AppLink.TokenIn(null));
    }

    [Theory]
    [InlineData("bocoach_corto", true)]
    [InlineData("BOCOACH_0123", true)]
    [InlineData("bocoach://conectar/bocoach_0123", true)]
    [InlineData("k7m2xq", false)]
    [InlineData("otra cosa", false)]
    public void Reconoce_lo_que_parece_un_token_aunque_este_incompleto(string text, bool expected)
    {
        Assert.Equal(expected, AppLink.LooksLikeToken(text));
    }
}
