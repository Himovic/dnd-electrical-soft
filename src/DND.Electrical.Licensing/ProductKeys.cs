namespace DND.Electrical.Licensing;

/// <summary>Public verification keys compiled into the product (no private key ever ships with the client).</summary>
public static class ProductKeys
{
    /// <summary>
    /// Development / evaluation signing key (ECDSA P-256). Its private counterpart is kept outside the source
    /// repository by D&amp;D | Data&amp;Development. Replace or complement it with the production key before release.
    /// </summary>
    public const string DevelopmentPublicKey = """
-----BEGIN PUBLIC KEY-----
MFkwEwYHKoZIzj0CAQYIKoZIzj0DAQcDQgAEFdVOhejPtylY/L5WI3JkfIO88xt7
2apGvzswDqSf5GGBxj9NKisMc/cvN0VAh33k/u06SL0IG7KsYj3/Pm/4nw==
-----END PUBLIC KEY-----
""";
}
