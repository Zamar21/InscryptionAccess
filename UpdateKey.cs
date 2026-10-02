// UpdateKey.cs
namespace IKMA
{
    /// <summary>
    /// The PUBLIC half of IKMA's update key. (Session 32. UNTESTED.)
    /// </summary>
    /// <remarks>
    /// make_update_key.ps1 (next to build.ps1) writes this file once, when it
    /// makes the key pair. The public half is safe to publish: it can only
    /// CHECK a signature, never make one. The private half lives only on
    /// Zamar's PC, in IKM Access\signing\, outside the repo.
    ///
    /// EMPTY = AUTO-UPDATE IS OFF. With no key built in, IKMA cannot tell a
    /// real update from a fake one, so it does not download anything. This is
    /// the state until the key is made.
    ///
    /// NEVER CHANGE THIS BY HAND. Every IKMA already installed checks updates
    /// against the key it was built with. A new key means every player has to
    /// install once by hand (the setup .exe) before auto-update works again.
    /// </remarks>
    internal static class UpdateKey
    {
        internal const string PublicKeyXml = "<RSAKeyValue><Modulus>4jrNQpAr4H8i+11xeyu+tAqsTj/vY5WyKZuiNdlvbWRz6OslNJFZkX3ehRwig/8FZAPBq2qhEMdt68UXtFIzpJY+45L6xkxj5kdV6NcMC1zpWdYzBtrTyn82DozoIj7bRPbpHww8dZOmw7zgoASiS9sTRA8mhx1BVmNRUOCdxKJGCJlQbX6wnCHP7UIinDS2rBaR/6PVNhHjN6mk5pOimeuxCOAzQMWmUsZCOBRfjlF3EnCuRUC32hMQnYCcrNbOa/gcpS+b4cFhuYczTFdmXyXX3I45n3GxnTyyOUPfjL05JG+TCoaVovikZULcFC9hxwBXdQCAT/2FpxGSe3jAZpCTNLYiyTsw52fx2j4I24o6lJXGm0wCliATLvr58Nu9AftaIH1I2fO404MB2s2oZvk3/w1lZ1xqN7J3ZzSjv6XSL0biSYSEhmGTgT/wPr490tppji4nm5kw0Dp6jQVV7AZYcSkY9GCh1XOUVE/h1Hirqq5x0eQyo40PSmRv0t8t</Modulus><Exponent>AQAB</Exponent></RSAKeyValue>";
    }
}
