using ASToolkit.Core;

namespace ASToolkit.Engine;

internal struct TrainInfo
{
    public TrainCars Cars;
    public float MaxBattleRange;
    public float MinBattleRange;
    public float Power;
    public float Weight;
    public float PoweredWeight;
    public int Speed;
    public int CarCount;
    public int Hp;
    public int MaxHp;
    public int FireDamage;
    public int BuildTime;
    public int ArmedCount;
    public int Rounds;
    public int MaxRounds;
    public int AmmoShare;
    private const float NoBattleRange = 999999;
    private const int NoSpeed = 10000;
    private const int FullShare = 100;
    private const int MinSpeed = 5;
    private const float AccelerationScale = 8;

    [EngineRoutine(0x00454170)]
    public TrainInfo(Engine engine)
    {
        Weight = 0;
        Power = 0;
        Speed = NoSpeed;
        Hp = 0;
        CarCount = 0;
        AmmoShare = 0;
        FireDamage = 0;
        ArmedCount = 0;
        BuildTime = 0;
        PoweredWeight = 0;
        MaxHp = 0;
        Cars &= ~(TrainCars.Other | TrainCars.AmmoWagon);
        MaxBattleRange = 0;
        Rounds = 0;
        MaxRounds = 0;
        MinBattleRange = NoBattleRange;
        for (var car = engine; car is not null; car = car.NextEngine)
        {
            AddEngine(car);
        }

        for (var car = engine.PrevEngine; car is not null; car = car.PrevEngine)
        {
            AddEngine(car);
        }

        AmmoShare = ArmedCount == 0 ? FullShare : AmmoShare / ArmedCount;
        if (MinBattleRange.IsExactly(NoBattleRange))
        {
            MinBattleRange = 0;
        }

        float pulling = Power - PoweredWeight;
        if (pulling != 0)
        {
            Speed = (int)((pulling - (Weight - PoweredWeight)) * Speed / pulling);
            if (Speed < MinSpeed)
            {
                Speed = 0;
            }
        }

        if (Speed == NoSpeed)
        {
            Speed = 0;
        }
    }

    [EngineRoutine(0x0043a8e0)]
    public readonly int Acceleration() => Weight != 0 ? (int)(Power / Weight * AccelerationScale) : 0;

    [EngineRoutine(0x00454260)]
    private void AddEngine(Engine car)
    {
        // Stub
    }
}
