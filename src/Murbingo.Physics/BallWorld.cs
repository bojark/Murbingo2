namespace Murbingo.Physics;
/// <summary>Stores the position, rotation and velocities of one numbered disk while the world controls its motion.</summary>
public sealed class RigidBall
{
    public int Number { get; }
    public double X { get; internal set; }
    public double Y { get; internal set; }
    public double VelocityX { get; internal set; }
    public double VelocityY { get; internal set; }
    public double Angle { get; internal set; }
    public double AngularVelocity { get; internal set; }

    internal double PreviousX, PreviousY;
    /// <summary>Assigns the immutable game number that links this visual disk to the remaining-number pool.</summary>
    internal RigidBall(int number) => Number = number;
}

/// <summary>Disk physics in metres, kilograms, seconds and radians. No UI or game RNG dependencies.</summary>
public sealed class BallWorld
{
    public const double Gravity = 9.81;
    public const double PixelsPerMetre = 100; // WPF logical pixels (DIP), not physical monitor pixels.
    public const double FixedStep = 1d / 120;
    public const double MaxFrameTime = .1;
    const int SolverIterations = 28;
    readonly List<RigidBall> bodies = new();
    readonly List<int> pending = new();
    readonly Random random;
    double accumulator, spawnTime, quietTime;
    int capacity;
    bool hasArea;
    public BallPhysicsSettings Settings { get; }
    public IReadOnlyList<RigidBall> Bodies => bodies;
    public IReadOnlyList<int> Pending => pending;
    public double Width { get; private set; }
    public double Height { get; private set; }
    public double Radius { get; private set; }
    public double Inertia => .5 * Settings.MassKg * Radius * Radius;
    public bool IsSleeping { get; private set; } = true;
    public bool NeedsUpdates => Settings.Enabled && hasArea && (!IsSleeping || pending.Count > 0);
    public long TotalSteps { get; private set; }

    /// <summary>Creates normalized simulation settings and a separate visual random generator that cannot influence game draws.</summary>
    public BallWorld(BallPhysicsSettings? settings = null, int? seed = null)
    {
        Settings = (settings ?? new()).Normalize();
        random = seed.HasValue ? new Random(seed.Value) : new Random();
    }

    /// <summary>Clears all old bodies and timing state, then creates a shuffled spawn queue for a new visual round.</summary>
    public void Reset(int count)
    {
        if (count is < 0 or > 99)
            throw new ArgumentOutOfRangeException(nameof(count));
        capacity = count;
        bodies.Clear();
        pending.Clear();
        var numbers = Enumerable.Range(1, count).ToArray();
        random.Shuffle(numbers);
        pending.AddRange(numbers);
        accumulator = spawnTime = quietTime = 0;
        TotalSteps = 0;
        IsSleeping = count == 0;
        UpdateRadius();
    }

    /// <summary>Removes drawn numbers from both existing bodies and future spawns and wakes the pile if support changes.</summary>
    public void Synchronize(IEnumerable<int> remaining)
    {
        var set = remaining.ToHashSet();
        bool changed = bodies.RemoveAll(b => !set.Contains(b.Number)) > 0;
        changed |= pending.RemoveAll(n => !set.Contains(n)) > 0;
        if (changed)
            Wake();
    }

    /// <summary>Rescales bodies and contact boundaries while preserving numbers; zero-sized viewports pause without crushing the pile.</summary>
    public void Resize(double widthDip, double heightDip)
    {
        if (!double.IsFinite(widthDip) || !double.IsFinite(heightDip))
            return;
        double width = Math.Max(0, widthDip / PixelsPerMetre), height = Math.Max(0, heightDip / PixelsPerMetre);
        // A collapsed/minimized viewport pauses the world without crushing it to a point.
        if (width == 0 || height == 0)
        {
            hasArea = false;
            accumulator = 0;
            return;
        }

        if (width == Width && height == Height && hasArea)
            return;
        hasArea = true;
        double oldWidth = Width, oldHeight = Height;
        Width = width;
        Height = height;
        UpdateRadius();
        foreach (var ball in bodies)
        {
            if (oldWidth > 0)
                ball.X *= Width / oldWidth;
            if (oldHeight > 0)
                ball.Y *= Height / oldHeight;
            ball.VelocityX = ball.VelocityY = ball.AngularVelocity = 0;
            ConstrainPosition(ball);
        }

        if (Width > 0 && Height > 0)
            for (int i = 0; i < 40; i++)
                SolveContacts(false);
        accumulator = 0;
        Wake();
    }

    /// <summary>Chooses a capacity-aware disk size with spare packing area so every ball can fit in an irregular pile.</summary>
    void UpdateRadius()
    {
        if (Width <= 0 || Height <= 0)
        {
            Radius = 0;
            return;
        }

        double best = 0;
        for (int columns = 1; columns <= Math.Max(1, capacity); columns++)
            best = Math.Max(best, Math.Min(Width / columns, Height / Math.Ceiling((double)Math.Max(1, capacity) / columns)));
        // Keep enough free area for an irregular settled pile rather than a jammed grid.
        Radius = Math.Min(.64, best * .76) / 2;
    }

    /// <summary>Clears the quiet timer when an external change requires the pile to move or finish spawning.</summary>
    public void Wake()
    {
        quietTime = 0;
        IsSleeping = bodies.Count == 0 && pending.Count == 0;
    }

    /// <summary>Converts a linear and angular impulse through mass and disk inertia, then wakes the affected world.</summary>
    public bool ApplyImpulse(int number, double x, double y, double angularImpulse = 0)
    {
        if (!double.IsFinite(x) || !double.IsFinite(y) || !double.IsFinite(angularImpulse))
            return false;
        var ball = bodies.FirstOrDefault(b => b.Number == number);
        if (ball is null || Inertia <= 0)
            return false;
        ball.VelocityX += x / Settings.MassKg;
        ball.VelocityY += y / Settings.MassKg;
        ball.AngularVelocity += angularImpulse / Inertia;
        Wake();
        return true;
    }

    /// <summary>Accumulates bounded real time into fixed simulation steps to avoid frame-rate-dependent motion and pause catch-up bursts.</summary>
    public void Advance(double elapsedSeconds)
    {
        if (!NeedsUpdates || !double.IsFinite(elapsedSeconds) || elapsedSeconds <= 0 || Radius <= 0)
            return;
        accumulator += Math.Min(elapsedSeconds, MaxFrameTime);
        int steps = 0;
        while (accumulator + 1e-12 >= FixedStep && steps++ < 12 && NeedsUpdates)
        {
            accumulator = Math.Max(0, accumulator - FixedStep);
            Step();
            TotalSteps++;
        }

        if (IsSleeping)
            accumulator = 0;
    }

    /// <summary>Spawns ready disks, integrates earth gravity and damping, solves contacts, and sleeps a settled pile.</summary>
    void Step()
    {
        spawnTime += FixedStep;
        double interval = Settings.SpawnIntervalMs / 1000d;
        int spawns = 0;
        while (pending.Count > 0 && spawnTime >= interval && spawns++ < 99)
        {
            if (!TrySpawn())
            {
                spawnTime = Math.Min(spawnTime, Math.Max(interval, FixedStep));
                break;
            }

            spawnTime = interval > 0 ? spawnTime - interval : 0;
        }

        double linear = Math.Exp(-Settings.LinearDamping * FixedStep), angular = Math.Exp(-Settings.AngularDamping * FixedStep);
        foreach (var ball in bodies)
        {
            ball.PreviousX = ball.X;
            ball.PreviousY = ball.Y;
            ball.VelocityY += Gravity * FixedStep;
            ball.VelocityX *= linear;
            ball.VelocityY *= linear;
            ball.AngularVelocity *= angular;
            // At most half a radius per step: prevent tunnelling through a disk or wall.
            double speed = Math.Sqrt(ball.VelocityX * ball.VelocityX + ball.VelocityY * ball.VelocityY);
            double maxSpeed = Radius * .5 / FixedStep;
            if (speed > maxSpeed)
            {
                ball.VelocityX *= maxSpeed / speed;
                ball.VelocityY *= maxSpeed / speed;
            }

            ball.X += ball.VelocityX * FixedStep;
            ball.Y += ball.VelocityY * FixedStep;
            ball.Angle = Math.IEEERemainder(ball.Angle + ball.AngularVelocity * FixedStep, 2 * Math.PI);
        }

        for (int i = 0; i < SolverIterations; i++)
            SolveContacts(true);
        // Stacked contact solvers retain tiny gravity-balancing velocities even
        // when position correction keeps the pile still. Sleep by actual motion.
        bool quiet = pending.Count == 0 && bodies.All(b => Math.Abs(b.X - b.PreviousX) < .00025 && Math.Abs(b.Y - b.PreviousY) < .00025 && Math.Abs(b.AngularVelocity) * Radius < .025);
        quietTime = quiet ? quietTime + FixedStep : 0;
        if (quietTime >= .65 || bodies.Count == 0 && pending.Count == 0)
        {
            IsSleeping = true;
            foreach (var b in bodies)
                b.VelocityX = b.VelocityY = b.AngularVelocity = 0;
        }
    }

    /// <summary>Finds an unoccupied location near the top before consuming a queued number, preventing overlapping new disks.</summary>
    bool TrySpawn()
    {
        double y = Radius + .001;
        int lanes = Math.Max(1, (int)(Width / (2 * Radius)));
        int start = random.Next(lanes);
        for (int i = 0; i < lanes; i++)
        {
            double x = Radius + (Width - 2 * Radius) * (((start + i) % lanes + .5) / lanes);
            if (bodies.Any(b => (b.X - x) * (b.X - x) + (b.Y - y) * (b.Y - y) < Math.Pow(2 * Radius + .001, 2)))
                continue;
            bodies.Add(new(pending[0]) { X = x, Y = y, VelocityX = (random.NextDouble() - .5) * .12, Angle = (random.NextDouble() - .5) * .55, AngularVelocity = (random.NextDouble() - .5) * 1.4 });
            pending.RemoveAt(0);
            quietTime = 0;
            return true;
        }

        return false;
    }

    /// <summary>Keeps each disk's entire radius inside the current physical boundaries after integration or contact correction.</summary>
    void ConstrainPosition(RigidBall ball)
    {
        if (Radius <= 0)
            return;
        ball.X = Math.Clamp(ball.X, Radius, Math.Max(Radius, Width - Radius));
        ball.Y = Math.Clamp(ball.Y, Radius, Math.Max(Radius, Height - Radius));
    }

    /// <summary>Separates intersecting disks and applies disk or wall responses repeatedly so dense piles remain stable.</summary>
    void SolveContacts(bool velocities)
    {
        double diameter = 2 * Radius;
        for (int i = 0; i < bodies.Count; i++)
        {
            var a = bodies[i];
            for (int j = i + 1; j < bodies.Count; j++)
            {
                var b = bodies[j];
                double dx = b.X - a.X, dy = b.Y - a.Y, d2 = dx * dx + dy * dy;
                if (d2 > diameter * diameter)
                    continue;
                double distance = Math.Sqrt(d2), nx = distance > 1e-10 ? dx / distance : (a.Number < b.Number ? 1 : -1), ny = distance > 1e-10 ? dy / distance : 0;
                double correction = Math.Max(0, diameter - distance - .0001) * .48;
                a.X -= nx * correction;
                a.Y -= ny * correction;
                b.X += nx * correction;
                b.Y += ny * correction;
                if (velocities)
                    DiskImpulse(a, b, nx, ny);
            }

            bool left = a.X <= Radius, right = a.X >= Width - Radius, top = a.Y <= Radius, bottom = a.Y >= Height - Radius;
            ConstrainPosition(a);
            if (velocities)
            {
                if (left)
                    WallImpulse(a, 1, 0);
                if (right)
                    WallImpulse(a, -1, 0);
                if (top)
                    WallImpulse(a, 0, 1);
                if (bottom)
                    WallImpulse(a, 0, -1);
            }
        }

        // Pair corrections can push an earlier body through a boundary in this pass.
        foreach (var ball in bodies)
            ConstrainPosition(ball);
    }

    /// <summary>Uses configured restitution while suppressing tiny nonideal impacts that would otherwise cause resting jitter.</summary>
    double Bounce(double normalVelocity) => normalVelocity < -.25 || Settings.Restitution == 1 ? Settings.Restitution : 0;
    /// <summary>Resolves disk collision and friction impulses using mass and rotational inertia, including rolling resistance.</summary>
    void DiskImpulse(RigidBall a, RigidBall b, double nx, double ny)
    {
        double inverseMass = 1 / Settings.MassKg, inverseInertia = 1 / Inertia;
        double vx = b.VelocityX - a.VelocityX + (a.AngularVelocity + b.AngularVelocity) * ny * Radius;
        double vy = b.VelocityY - a.VelocityY - (a.AngularVelocity + b.AngularVelocity) * nx * Radius;
        double vn = vx * nx + vy * ny;
        if (vn >= 0)
            return;
        double impulse = -(1 + Bounce(vn)) * vn / (2 * inverseMass);
        double tx = -ny, ty = nx, vt = vx * tx + vy * ty;
        double friction = Math.Clamp(-vt / (2 * inverseMass + 2 * Radius * Radius * inverseInertia), -Settings.Friction * impulse, Settings.Friction * impulse);
        double ix = nx * impulse + tx * friction, iy = ny * impulse + ty * friction;
        a.VelocityX -= ix * inverseMass;
        a.VelocityY -= iy * inverseMass;
        b.VelocityX += ix * inverseMass;
        b.VelocityY += iy * inverseMass;
        double torque = Radius * (nx * iy - ny * ix) * inverseInertia;
        a.AngularVelocity -= torque;
        b.AngularVelocity -= torque;
        double rollingResistance = impulse * Radius * Settings.Friction * .025 * inverseInertia;
        a.AngularVelocity = ReduceSpin(a.AngularVelocity, rollingResistance);
        b.AngularVelocity = ReduceSpin(b.AngularVelocity, rollingResistance);
    }

    /// <summary>Resolves boundary impacts and contact friction without transferring motion to the immovable panel walls.</summary>
    void WallImpulse(RigidBall ball, double nx, double ny)
    {
        double vn = ball.VelocityX * nx + ball.VelocityY * ny;
        if (vn >= 0)
            return;
        double inverseMass = 1 / Settings.MassKg, inverseInertia = 1 / Inertia;
        double impulse = -(1 + Bounce(vn)) * vn / inverseMass;
        double tx = -ny, ty = nx;
        double vt = ball.VelocityX * tx + ball.VelocityY * ty - ball.AngularVelocity * Radius;
        double friction = Math.Clamp(-vt / (inverseMass + Radius * Radius * inverseInertia), -Settings.Friction * impulse, Settings.Friction * impulse);
        ball.VelocityX += (nx * impulse + tx * friction) * inverseMass;
        ball.VelocityY += (ny * impulse + ty * friction) * inverseMass;
        ball.AngularVelocity -= Radius * friction * inverseInertia;
        ball.AngularVelocity = ReduceSpin(ball.AngularVelocity, impulse * Radius * Settings.Friction * .025 * inverseInertia);
    }

    /// <summary>Dissipates a bounded amount of angular speed without reversing spin or adding energy.</summary>
    static double ReduceSpin(double velocity, double amount) => Math.Sign(velocity) * Math.Max(0, Math.Abs(velocity) - amount);
}
