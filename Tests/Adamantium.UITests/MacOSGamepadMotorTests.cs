using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Adamantium.UI.Platforms.MacOS;
using NUnit.Framework;

namespace Adamantium.UITests;

/// <summary>A gamepad's motor runs off the thread that asks, as last asked, and is let go of for good when the gamepad is
/// disconnected.</summary>
[TestFixture]
public class MacOSGamepadMotorTests
{
    [Test]
    public async Task ItRunsAsAsked_AndStandsStillAtZero_ReadyToRunAgain()
    {
        var motor = new Motor();
        var gamepadMotor = new MacOSGamepadMotor(motor);

        await Ask(gamepadMotor, 0.5f);
        await Ask(gamepadMotor, 0);
        await Ask(gamepadMotor, 0.7f);

        Assert.Multiple(() =>
        {
            Assert.That(motor.Runs, Is.EqualTo(new[] { 0.5f, 0, 0.7f }));
            Assert.That(motor.LetGoes, Is.Zero);
        });
    }

    [Test]
    public async Task ItRunsAsFineAsItWasMade_UntilToldOtherwise()
    {
        var motor = new Motor();
        var gamepadMotor = new MacOSGamepadMotor(motor, sharpness: 1);

        await Ask(gamepadMotor, 0.5f);
        gamepadMotor.SetSharpness(0.3f);
        await gamepadMotor.Applying;

        Assert.Multiple(() =>
        {
            Assert.That(motor.Runs, Is.EqualTo(new[] { 0.5f, 0.5f }), "a run in hand takes the new sharpness at once");
            Assert.That(motor.Sharpnesses, Is.EqualTo(new[] { 1f, 0.3f }));
        });
    }

    [Test]
    public async Task SharpnessChangedStandingStill_WaitsForTheNextRun()
    {
        var motor = new Motor();
        var gamepadMotor = new MacOSGamepadMotor(motor);

        gamepadMotor.SetSharpness(0.6f);
        await gamepadMotor.Applying;
        Assert.That(motor.Runs, Is.Empty, "nothing runs for a sharpness alone");

        await Ask(gamepadMotor, 0.4f);
        Assert.That(motor.Sharpnesses, Is.EqualTo(new[] { 0.6f }));
    }

    [Test]
    public async Task TheSameAskedAgain_IsNotSentAgain()
    {
        var motor = new Motor();
        var gamepadMotor = new MacOSGamepadMotor(motor);

        for (var frame = 0; frame < 100; frame++)
            gamepadMotor.Run(0.5f);

        await gamepadMotor.Applying;
        await Ask(gamepadMotor, 0.5f);
        await Ask(gamepadMotor, 0);

        Assert.That(motor.Runs, Is.EqualTo(new[] { 0.5f, 0 }));
    }

    [Test]
    public async Task WhatWasAskedWhileItStarts_IsSkipped_ForTheLast()
    {
        var motor = new Motor { Gate = new ManualResetEventSlim() };
        var gamepadMotor = new MacOSGamepadMotor(motor);

        gamepadMotor.Run(0.1f);
        motor.Entered.Wait();
        gamepadMotor.Run(0.2f);
        gamepadMotor.Run(0.9f);
        motor.Gate.Set();
        await gamepadMotor.Applying;

        Assert.That(motor.Runs, Is.EqualTo(new[] { 0.1f, 0.9f }));
    }

    [Test]
    public async Task ARunEndedBeforeItBegan_IsFeltForAWhile()
    {
        var motor = new Motor { Gate = new ManualResetEventSlim() };
        var gamepadMotor = new MacOSGamepadMotor(motor) { ShortestRun = TimeSpan.FromMilliseconds(60) };

        gamepadMotor.Run(0.5f);
        motor.Entered.Wait();
        gamepadMotor.Run(0);
        motor.Gate.Set();
        await gamepadMotor.Applying;

        Assert.Multiple(() =>
        {
            Assert.That(motor.Runs, Is.EqualTo(new[] { 0.5f, 0 }));
            Assert.That(motor.Times[1] - motor.Times[0], Is.GreaterThan(TimeSpan.FromMilliseconds(50)));
        });
    }

    // The second pulse comes and goes while the first is being ended.
    [Test]
    public async Task APulseWhileTheLastIsEnded_IsRun()
    {
        var motor = new Motor { Gate = new ManualResetEventSlim(true) };
        var gamepadMotor = new MacOSGamepadMotor(motor);
        await Ask(gamepadMotor, 0.5f);
        motor.Gate.Reset();
        motor.Entered.Reset();

        gamepadMotor.Run(0);
        motor.Entered.Wait();
        gamepadMotor.Run(0.8f);
        gamepadMotor.Run(0);
        motor.Gate.Set();
        await gamepadMotor.Applying;

        Assert.That(motor.Runs, Is.EqualTo(new[] { 0.5f, 0, 0.8f, 0 }));
    }

    [Test]
    public async Task StoppedByTheSystem_ItRunsAgain()
    {
        var motor = new Motor();
        var gamepadMotor = new MacOSGamepadMotor(motor) { AfterFailure = TimeSpan.FromMilliseconds(200) };
        await Ask(gamepadMotor, 0.5f);

        motor.WasStopped = true;
        gamepadMotor.Tick();
        await gamepadMotor.Applying;
        Assert.That(motor.Runs, Has.Count.EqualTo(1), "stopped as soon as started, it waits");
        await Task.Delay(300);
        gamepadMotor.Tick();
        await gamepadMotor.Applying;
        gamepadMotor.Tick();
        await gamepadMotor.Applying;

        Assert.That(motor.Runs, Is.EqualTo(new[] { 0.5f, 0.5f }));
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task ThatCannotBeRun_IsTriedAgainOnlyAfterAWhile(bool throws)
    {
        var motor = new Motor { Fails = true, Throws = throws };
        var gamepadMotor = new MacOSGamepadMotor(motor) { AfterFailure = TimeSpan.FromMilliseconds(200) };

        for (var frame = 0; frame < 10; frame++)
            await Ask(gamepadMotor, 0.1f * frame);
        var atOnce = motor.Runs.Count;
        motor.Fails = false;
        await Task.Delay(300);
        gamepadMotor.Tick();
        await gamepadMotor.Applying;

        Assert.Multiple(() =>
        {
            Assert.That(atOnce, Is.EqualTo(1));
            Assert.That(motor.Runs[^1], Is.EqualTo(0.9f).Within(1e-6f), "as last asked");
            Assert.That(motor.Runs, Has.Count.EqualTo(2));
        });
    }

    [Test]
    public async Task StandingStillForLong_ItIsLetGoOf()
    {
        var motor = new Motor();
        var gamepadMotor = new MacOSGamepadMotor(motor) { Ready = TimeSpan.FromMilliseconds(100) };
        await Ask(gamepadMotor, 0.5f);
        await Task.Delay(200);
        gamepadMotor.Tick();
        await gamepadMotor.Applying;
        var whileRunning = motor.LetGoes;
        await Ask(gamepadMotor, 0);
        await Task.Delay(200);
        gamepadMotor.Tick();
        await gamepadMotor.Applying;
        gamepadMotor.Tick();
        await gamepadMotor.Applying;

        Assert.Multiple(() =>
        {
            Assert.That(whileRunning, Is.Zero);
            Assert.That(motor.LetGoes, Is.EqualTo(1));
        });
    }

    [Test]
    public async Task Closed_ItIsLetGoOf_EvenWhileStarting()
    {
        var motor = new Motor { Gate = new ManualResetEventSlim() };
        var gamepadMotor = new MacOSGamepadMotor(motor);

        gamepadMotor.Run(1);
        motor.Entered.Wait();
        var closed = gamepadMotor.Close();
        motor.Gate.Set();
        await closed;
        await Ask(gamepadMotor, 1);

        Assert.Multiple(() =>
        {
            Assert.That(motor.LetGoes, Is.EqualTo(1));
            Assert.That(motor.Runs, Has.Count.EqualTo(1), "nothing runs on what is gone");
        });
    }

    [Test]
    public async Task APlaceWithNoMotor_DoesNothing()
    {
        var gamepadMotor = new MacOSGamepadMotor(null);

        await Ask(gamepadMotor, 1);
        gamepadMotor.Tick();

        Assert.DoesNotThrowAsync(gamepadMotor.Close);
    }

    [Test]
    public async Task Strengths_AreHeldWithinTheMotors()
    {
        var motor = new Motor();
        var gamepadMotor = new MacOSGamepadMotor(motor);

        await Ask(gamepadMotor, 7);
        await Ask(gamepadMotor, float.NaN);

        Assert.That(motor.Runs, Is.EqualTo(new[] { 1f, 0 }));
    }

    private static Task Ask(MacOSGamepadMotor motor, float strength)
    {
        motor.Run(strength);
        return motor.Applying;
    }

    private sealed class Motor : IHapticMotor
    {
        public List<float> Runs { get; } = [];

        public List<TimeSpan> Times { get; } = [];

        public int LetGoes { get; private set; }

        public bool WasStopped { get; set; }

        public bool Fails { get; set; }

        public bool Throws { get; init; }

        public ManualResetEventSlim Gate { get; init; }

        public ManualResetEventSlim Entered { get; } = new();

        public List<float> Sharpnesses { get; } = [];

        public bool Run(float strength, float sharpness)
        {
            Runs.Add(strength);
            Sharpnesses.Add(sharpness);
            Entered.Set();
            Gate?.Wait();
            Times.Add(TimeSpan.FromMilliseconds(Environment.TickCount64));
            WasStopped = false;
            return Fails && Throws ? throw new InvalidOperationException() : !Fails;
        }

        public void LetGo() => LetGoes++;
    }
}
