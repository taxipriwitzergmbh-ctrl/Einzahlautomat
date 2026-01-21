namespace TaMi_Einzahlautomat.Coins
{
    public enum CoinValidatorType
    {
        None = 0,
        SmartCoinV1 = 1,
        SmartCoinV2 = 2,
        // veraltet: wird in der Factory auf SmartCoinV2 gemappt, keine eigene Klasse mehr
        RtCoinSystem = 3,
        Rm5Cctalk = 4 // NEU: RM5 ccTalk Münzprüfer
    }
}