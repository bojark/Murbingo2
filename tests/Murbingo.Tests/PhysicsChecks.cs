using System.Text.Json;
using Murbingo.Core;
using Murbingo.Physics;

/// <summary>Runs deterministic simulation regressions without opening WPF or involving the user's visual testing workflow.</summary>
internal static class PhysicsChecks
{
    /// <summary>Checks spawning, contacts, sleep, configuration and game independence across representative physical worlds.</summary>
    public static void Run(Action<bool, string> check)
    {
        // Purpose: Feeds repeatable frame durations into a world so tests can advance virtual time without real-time waiting.
        static void Simulate(BallWorld world, double seconds)
        {
            for (int i = 0; i < (int)(seconds * 60); i++)
                world.Advance(1d / 60);
        }

        // Purpose: Creates a seeded physical test world with a known viewport and round capacity.
        static BallWorld World(int count, double width = 480, double height = 180, BallPhysicsSettings? settings = null, int seed = 7)
        {
            var w = new BallWorld(settings, seed);
            w.Resize(width, height);
            w.Reset(count);
            return w;
        }

        // Purpose: Checks finite coordinates and full-radius containment to detect escaped or corrupted disks.
        static bool Inside(BallWorld w) => w.Bodies.All(b => double.IsFinite(b.X + b.Y + b.Angle) && b.X >= w.Radius - 1e-9 && b.X <= w.Width - w.Radius + 1e-9 && b.Y >= w.Radius - 1e-9 && b.Y <= w.Height - w.Radius + 1e-9);
        // Purpose: Measures worst disk penetration so tests enforce a numeric contact tolerance rather than judging screenshots.
        static double Overlap(BallWorld w)
        {
            double maximum = 0;
            for (int i = 0; i < w.Bodies.Count; i++)
                for (int j = i + 1; j < w.Bodies.Count; j++)
                {
                    var a = w.Bodies[i];
                    var b = w.Bodies[j];
                    maximum = Math.Max(maximum, 2 * w.Radius - Math.Sqrt(Math.Pow(a.X - b.X, 2) + Math.Pow(a.Y - b.Y, 2)));
                }

            return maximum;
        }

        var empty = World(0);
        Simulate(empty, 2);
        check(empty.IsSleeping && empty.Bodies.Count == 0 && empty.Pending.Count == 0 && empty.TotalSteps == 0, "physics empty round remains idle");
        var single = World(1);
        Simulate(single, 10);
        check(single.Bodies.Count == 1 && single.Pending.Count == 0 && Inside(single) && single.IsSleeping, "one ball falls to the bottom and sleeps");
        check(Math.Abs(single.Bodies[0].Y - (single.Height - single.Radius)) < .001, "one ball rests on the floor");
        foreach (var dimensions in new[]
        {
            (400d, 150d),
            (480d, 180d),
            (900d, 320d)
        }

        )
        {
            var pile = World(99, dimensions.Item1, dimensions.Item2);
            Simulate(pile, 30);
            check(pile.Bodies.Count == 99 && pile.Pending.Count == 0 && pile.Bodies.Select(b => b.Number).Distinct().Count() == 99 && Inside(pile), $"99 balls fill and stay within {dimensions}");
            double overlap = Overlap(pile);
            check(overlap < pile.Radius * .05, $"settled pile overlap <5% radius at {dimensions} (actual {overlap / pile.Radius:P2})");
            check(pile.IsSleeping, $"99-ball pile sleeps at {dimensions}");
        }

        var removal = World(99);
        Simulate(removal, 30);
        var removed = removal.Bodies.OrderByDescending(b => removal.Bodies.Count(other => other.Y < b.Y - removal.Radius * .5 && Math.Pow(other.X - b.X, 2) + Math.Pow(other.Y - b.Y, 2) < Math.Pow(removal.Radius * 2.1, 2))).ThenBy(b => Math.Abs(b.X - removal.Width / 2)).First();
        var before = removal.Bodies.ToDictionary(b => b.Number, b => (b.X, b.Y));
        removal.Synchronize(Enumerable.Range(1, 99).Where(n => n != removed.Number));
        check(!removal.IsSleeping && removal.Bodies.Count == 98 && removal.Bodies.All(b => b.Number != removed.Number), "removing a settled ball wakes the pile and deletes its body");
        Simulate(removal, 20);
        check(Inside(removal) && Overlap(removal) < removal.Radius * .05, "99-ball pile remains valid after removing a supporting ball");
        var supported = World(2, 70, 250);
        Simulate(supported, 10);
        var support = supported.Bodies.MaxBy(b => b.Y)!;
        var above = supported.Bodies.MinBy(b => b.Y)!;
        double oldY = above.Y;
        supported.Synchronize(new[] { above.Number });
        Simulate(supported, 8);
        check(above.Y > oldY + supported.Radius && supported.IsSleeping, "unsupported upper ball falls and settles after its support is removed");
        var queued = World(99);
        int queuedNumber = queued.Pending[0];
        queued.Synchronize(Enumerable.Range(1, 99).Where(n => n != queuedNumber));
        Simulate(queued, 20);
        check(queued.Bodies.Count == 98 && queued.Bodies.All(b => b.Number != queuedNumber) && queued.Pending.Count == 0, "draw before spawning removes the number from its queue");
        var restart = World(99);
        Simulate(restart, .5);
        restart.Reset(5);
        check(restart.Bodies.Count == 0 && restart.Pending.Count == 5 && restart.TotalSteps == 0, "restart during filling clears bodies, queue and accumulated steps");
        Simulate(restart, 10);
        restart.Reset(0);
        Simulate(restart, 2);
        check(restart.Bodies.Count == 0 && restart.Pending.Count == 0 && restart.IsSleeping, "restart after movement to zero cannot revive old balls");
        var resize = World(99, 900, 320);
        Simulate(resize, 15);
        resize.Resize(400, 150);
        Simulate(resize, 25);
        check(resize.Bodies.Count == 99 && Inside(resize) && Overlap(resize) < resize.Radius * .05, "shrink settled area preserves numbers and resolves contacts");
        resize.Resize(0, 0);
        resize.Advance(3);
        resize.Resize(700, 250);
        Simulate(resize, 25);
        check(resize.Bodies.Count == 99 && Inside(resize) && resize.IsSleeping, "zero-size pause and expansion retain the round");
        var settings = JsonSerializer.Deserialize<BallPhysicsSettings>("""{"massKg":-4,"restitution":0.7,"friction":"bad","linearDamping":2.5,"angularDamping":null,"spawnIntervalMs":-1,"enabled":false}""")!;
        check(settings.MassKg == .15 && settings.Restitution == .7 && settings.Friction == .4 && settings.LinearDamping == 2.5 && settings.AngularDamping == 1.2 && settings.SpawnIntervalMs == 35 && !settings.Enabled, "invalid physics fields fall back independently without losing valid values");
        check(new BallPhysicsSettings { MassKg = double.NaN, Restitution = 2, Friction = -1, LinearDamping = double.PositiveInfinity }.Normalize() == new BallPhysicsSettings(), "nonfinite and out-of-range direct settings normalize safely");
        check(JsonSerializer.Deserialize<BallPhysicsSettings>("null") == new BallPhysicsSettings() && JsonSerializer.Deserialize<BallPhysicsSettings>("{}") == new BallPhysicsSettings(), "null and missing physics configuration use defaults");
        var off = World(99, settings: new() { Enabled = false });
        Simulate(off, 3);
        check(!off.NeedsUpdates && off.Bodies.Count == 0 && off.TotalSteps == 0, "disabled physics performs no calculations");
        var light = World(1, 400, 400, new() { MassKg = .15 });
        var heavy = World(1, 400, 400, new() { MassKg = .6 });
        Simulate(light, .2);
        Simulate(heavy, .2);
        check(Math.Abs(light.Bodies[0].Y - heavy.Bodies[0].Y) < 1e-10 && BallWorld.Gravity == 9.81 && BallWorld.PixelsPerMetre == 100, "earth gravity and fall trajectory do not depend on mass");
        double lightV = light.Bodies[0].VelocityX, heavyV = heavy.Bodies[0].VelocityX, lightW = light.Bodies[0].AngularVelocity, heavyW = heavy.Bodies[0].AngularVelocity;
        light.ApplyImpulse(light.Bodies[0].Number, .02, 0, .001);
        heavy.ApplyImpulse(heavy.Bodies[0].Number, .02, 0, .001);
        check(Math.Abs((light.Bodies[0].VelocityX - lightV) / (heavy.Bodies[0].VelocityX - heavyV) - 4) < 1e-8 && Math.Abs((light.Bodies[0].AngularVelocity - lightW) / (heavy.Bodies[0].AngularVelocity - heavyW) - 4) < 1e-8, "mass and disk inertia determine linear and angular impulse responses");
        // Purpose: Measures upward speed after a floor collision to verify that restitution affects the physical response.
        double Bounce(double restitution)
        {
            var w = World(1, 400, 400, new() { Restitution = restitution, LinearDamping = 0, AngularDamping = 0, Friction = 0 });
            double fastest = 0;
            for (int i = 0; i < 300; i++)
            {
                w.Advance(BallWorld.FixedStep);
                if (w.Bodies.Count > 0)
                    fastest = Math.Min(fastest, w.Bodies[0].VelocityY);
            }

            return -fastest;
        }

        check(Bounce(.8) > Bounce(.05) * 4, "higher restitution produces a stronger floor bounce");
        var pause = World(99);
        pause.Advance(120);
        check(pause.TotalSteps <= 12 && pause.Bodies.Count <= 3, "long frame pauses cannot accumulate unbounded simulation work");
        var asleep = World(1);
        Simulate(asleep, 10);
        long steps = asleep.TotalSteps;
        Simulate(asleep, 10);
        check(asleep.TotalSteps == steps, "sleeping pile consumes no further simulation steps");
        var game = new Game(99);
        var visual = World(99);
        Simulate(visual, 2);
        check(game.Drawn.Count == 0 && game.Remaining.Count == 99 && game.Last == null, "advancing physics does not mutate game state");
        bool consistent = true;
        while (game.Draw() != null)
        {
            visual.Synchronize(game.Remaining);
            visual.Advance(1d / 60);
            consistent &= visual.Bodies.Select(b => b.Number).Concat(visual.Pending).Order().SequenceEqual(game.Remaining);
        }

        check(consistent, "body and spawn-queue union matches remaining game numbers after every draw");
        check(game.Drawn.SequenceEqual(Enumerable.Range(1, 99)) && visual.Bodies.Count == 0 && visual.Pending.Count == 0, "full game draws all numbers independently while bodies and queue are removed");
        var stress = true;
        for (int seed = 0; seed < 12; seed++)
        {
            var pile = World(99, 400, 150, seed: seed);
            Simulate(pile, 30);
            bool valid = pile.Bodies.Count == 99 && pile.Pending.Count == 0 && Inside(pile) && Overlap(pile) < pile.Radius * .05 && pile.IsSleeping;
            if (!valid)
                Console.WriteLine($"Physics diagnostic seed {seed}: bodies={pile.Bodies.Count}, queue={pile.Pending.Count}, overlap={Overlap(pile) / pile.Radius:P2}, sleep={pile.IsSleeping}, max spin surface speed={pile.Bodies.Max(b => Math.Abs(b.AngularVelocity) * pile.Radius):F6}");
            stress &= valid;
        }

        check(stress, "12 additional randomized 99-ball piles stay bounded, settle and sleep");
        var fast = World(99, settings: new() { SpawnIntervalMs = 0 });
        Simulate(fast, 20);
        check(fast.Bodies.Count == 99 && fast.Pending.Count == 0 && Inside(fast) && Overlap(fast) < fast.Radius * .05, "zero spawn interval fills without losing or interpenetrating balls");
        var frameA = World(99);
        var frameB = World(99);
        for (int i = 0; i < 720; i++)
            frameA.Advance(1d / 120);
        for (int i = 0; i < 360; i++)
            frameB.Advance(1d / 60);
        check(frameA.Bodies.Count == frameB.Bodies.Count && frameA.Bodies.Zip(frameB.Bodies).All(p => p.First.Number == p.Second.Number && Math.Abs(p.First.X - p.Second.X) + Math.Abs(p.First.Y - p.Second.Y) < 1e-8), "fixed simulation produces identical states under 60 and 120 Hz frame schedules");
        var resizedFilling = World(99);
        Simulate(resizedFilling, .25);
        resizedFilling.Resize(400, 150);
        Simulate(resizedFilling, 25);
        check(resizedFilling.Bodies.Count == 99 && resizedFilling.Pending.Count == 0 && Inside(resizedFilling), "resizing while balls are still spawning preserves the queue and bodies");
    }
}
