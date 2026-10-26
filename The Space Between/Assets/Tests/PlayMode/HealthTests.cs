using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace TheSpaceBetween.Tests
{
    public class HealthTests : PlayModeTestBase
    {
        private HealthData CreateData(float maxHealth = 100f, float damageCooldown = 0f)
        {
            HealthData data = Track(ScriptableObject.CreateInstance<HealthData>());
            SetField(data, "maxHealth", maxHealth);
            SetField(data, "damageCooldown", damageCooldown);
            return data;
        }

        private Health CreateHealth(HealthData data)
        {
            Health health = CreateInactive<Health>("Health Test");
            SetField(health, "data", data);
            health.gameObject.SetActive(true);
            return health;
        }

        [Test]
        public void StartsAtFullHealth()
        {
            Health health = CreateHealth(CreateData(80f));

            Assert.AreEqual(80f, health.CurrentHealth);
            Assert.AreEqual(1f, health.HealthRatio);
            Assert.IsFalse(health.IsDead);
        }

        [Test]
        public void TakeDamage_ReducesCurrentHealth()
        {
            Health health = CreateHealth(CreateData(100f));

            health.TakeDamage(30f);

            Assert.AreEqual(70f, health.CurrentHealth);
            Assert.AreEqual(0.7f, health.HealthRatio, 0.0001f);
        }

        [Test]
        public void TakeDamage_ReportsTheAmountThroughTheDamagedEvent()
        {
            Health health = CreateHealth(CreateData(100f));
            float reported = -1f;
            health.Damaged += amount => reported = amount;

            health.TakeDamage(12f);

            Assert.AreEqual(12f, reported);
        }

        [Test]
        public void TakeDamage_ToZero_KillsOnceAndRaisesDiedOnce()
        {
            Health health = CreateHealth(CreateData(50f));
            int diedCount = 0;
            health.Died += () => diedCount++;

            health.TakeDamage(50f);
            health.TakeDamage(10f);

            Assert.IsTrue(health.IsDead);
            Assert.AreEqual(0f, health.CurrentHealth);
            Assert.AreEqual(1, diedCount);
        }

        [Test]
        public void TakeDamage_IgnoresZeroAndNegativeAmounts()
        {
            Health health = CreateHealth(CreateData(100f));

            health.TakeDamage(0f);
            health.TakeDamage(-25f);

            Assert.AreEqual(100f, health.CurrentHealth);
        }

        [Test]
        public void TakeDamage_InsideTheCooldown_IsIgnored()
        {
            Health health = CreateHealth(CreateData(100f, damageCooldown: 10f));

            health.TakeDamage(20f);
            health.TakeDamage(20f);

            Assert.AreEqual(80f, health.CurrentHealth);
        }

        [Test]
        public void Heal_RestoresHealthButNeverAboveTheMaximum()
        {
            Health health = CreateHealth(CreateData(100f));
            health.TakeDamage(40f);

            health.Heal(15f);
            Assert.AreEqual(75f, health.CurrentHealth);

            health.Heal(500f);
            Assert.AreEqual(100f, health.CurrentHealth);
        }

        [Test]
        public void Heal_ReportsOnlyTheAmountActuallyHealed()
        {
            Health health = CreateHealth(CreateData(100f));
            health.TakeDamage(10f);
            float reported = -1f;
            health.Healed += amount => reported = amount;

            health.Heal(50f);

            Assert.AreEqual(10f, reported);
        }

        [Test]
        public void Heal_IsIgnoredWhenDead()
        {
            Health health = CreateHealth(CreateData(100f));
            health.TakeDamage(100f);

            health.Heal(50f);

            Assert.IsTrue(health.IsDead);
            Assert.AreEqual(0f, health.CurrentHealth);
        }

        [Test]
        public void Revive_RestoresFullHealthAndClearsDeath()
        {
            Health health = CreateHealth(CreateData(100f));
            health.TakeDamage(100f);

            health.Revive();

            Assert.IsFalse(health.IsDead);
            Assert.AreEqual(100f, health.CurrentHealth);
        }

        [Test]
        public void MissingData_LogsAnErrorDisablesTheComponentAndIgnoresDamage()
        {
            LogAssert.Expect(LogType.Error, new Regex("has no HealthData assigned"));

            Health health = CreateHealth(null);

            Assert.IsFalse(health.enabled);
            Assert.DoesNotThrow(() => health.TakeDamage(10f));
            Assert.IsFalse(health.IsDead);
        }
    }
}
