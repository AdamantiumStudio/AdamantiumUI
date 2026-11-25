using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Adamantium.UI.Platforms.MacOS;
using NUnit.Framework;

namespace Adamantium.UITests;

/// <summary>A gamepad's motors run off the thread that asks, each as last asked, and stop for good when it is
/// disconnected.</summary>
[TestFixture]
public class MacOSGamepadMotorsTests
{
    [Test]
    public async Task AMotor_RunsAsAsked_AndStopsAtZero()
    {
        var motor = new Motor();
        var motors = new MacOSGamepadMotors(motor);

        motors.Run(0, 0.5f);
        await motors.Applying;
        motors.Run(0, 0);
        await motors.Applying;

        Assert.Multiple(() =>
        {
            Assert.That(motor.Runs, Is.EqualTo(new[] { 0.5f }));
            Assert.That(motor.Stops, Is.EqualTo(1));
        });
    }

    [Test]
    public async Task TheSameAskedAgain_IsNotSentAgain()
    {
        var motor = new Motor();
        var motors = new MacOSGamepadMotors(motor);

        for (var frame = 0; frame < 100; frame++)
        {
            motors.Run(0, 0.5f);
        }

        await motors.Applying;
        motors.Run(0, 0.5f);
        await motors.Applying;

        Assert.That(motor.Runs, Is.EqualTo(new[] { 0.5f }));
    }

    [Test]
    public async Task WhatWasAskedWhileAMotorStarts_IsSkipped_ForTheLast()
    {
        var motor = new Motor { Gate = new ManualResetEventSlim() };
        var motors = new MacOSGamepadMotors(motor);

        motors.Run(0, 0.1f);
        motor.Entered.Wait();
        motors.Run(0, 0.2f);
        motors.Run(0, 0.9f);
        motor.Gate.Set();
        await motors.Applying;

        Assert.That(motor.Runs, Is.EqualTo(new[] { 0.1f, 0.9f }));
    }

    [Test]
    public async Task Closed_ItStopsWhatRuns_EvenWhatIsStarting()
    {
        var motor = new Motor { Gate = new ManualResetEventSlim() };
        var motors = new MacOSGamepadMotors(motor, null);

        motors.Run(0, 1);
        motors.Run(1, 1);
        motor.Entered.Wait();
        motors.Close();
        motor.Gate.Set();
        await motors.Applying;
        motors.Run(0, 1);
        await motors.Applying;

        Assert.Multiple(() =>
        {
            Assert.That(motor.Stops, Is.EqualTo(1));
            Assert.That(motor.Runs, Has.Count.EqualTo(1), "nothing runs on what is gone");
        });
    }

    [Test]
    public async Task Strengths_AreHeldWithinTheMotors()
    {
        var motor = new Motor();
        var motors = new MacOSGamepadMotors(motor);

        motors.Run(0, 7);
        await motors.Applying;
        motors.Run(0, float.NaN);
        await motors.Applying;

        Assert.Multiple(() =>
        {
            Assert.That(motor.Runs, Is.EqualTo(new[] { 1f }));
            Assert.That(motor.Stops, Is.EqualTo(1));
        });
    }

    private sealed class Motor : IHapticMotor
    {
        public List<float> Runs { get; } = [];

        public int Stops { get; private set; }

        public ManualResetEventSlim Gate { get; init; }

        public ManualResetEventSlim Entered { get; } = new();

        public bool Run(float strength)
        {
            Runs.Add(strength);
            Entered.Set();
            Gate?.Wait();
            return true;
        }

        public void Stop() => Stops++;
    }
}
