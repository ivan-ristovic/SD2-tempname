﻿using System;

namespace doodLbot.Common
{
    /// <summary>
    /// Used to prevent an action from being executed more than once in a given time span.
    /// </summary>
    public class RateLimiter
    {
        private bool isCooldownActive;
        private DateTimeOffset resetTime;
        private readonly TimeSpan cooldownTimeout;


        /// <summary>
        /// Constructs a new RateLimiter object that resets the cooldown after givens time span.
        /// </summary>
        /// <param name="cooldown">Time span after the cooldown will be reset.</param>
        public RateLimiter(TimeSpan timespan)
        {
            cooldownTimeout = timespan;
            resetTime = DateTimeOffset.UtcNow + cooldownTimeout;
            isCooldownActive = false;
        }

        /// <summary>
        /// Constructs a new RateLimiter object that resets the cooldown after given time span in seconds.
        /// </summary>
        /// <param name="seconds">Amount of seconds after the cooldown will be reset.</param>
        public RateLimiter(double seconds)
            : this(TimeSpan.FromSeconds(seconds))
        {

        }


        /// <summary>
        /// Checks if the cooldown is active for this instance.
        /// </summary>
        /// <returns></returns>
        public bool IsCooldownActive()
        {
            var success = false;

            var now = DateTimeOffset.UtcNow;
            if (now >= resetTime)
            {
                isCooldownActive = false;
                resetTime = now + cooldownTimeout;
            }

            if (!isCooldownActive)
            {
                isCooldownActive = true;
                success = true;
            }

            return !success;
        }
    }
}
