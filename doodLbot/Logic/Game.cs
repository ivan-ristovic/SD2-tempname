﻿using doodLbot.Common;
using doodLbot.Entities;
using doodLbot.Entities.CodeElements;
using doodLbot.Entities.CodeElements.ConditionElements;
using doodLbot.Extensions;
using doodLbot.Hubs;

using Microsoft.AspNetCore.SignalR;
using Serilog;
using System;
using System.Collections.Generic;
using System.Threading;

namespace doodLbot.Logic
{
    /// <summary>
    /// Represents a doodLbot game.
    /// </summary>
    public sealed class Game
    {
        public static readonly double TickRate = Design.TickRate;

        public static TimeSpan RefreshTimeSpan => TimeSpan.FromMilliseconds(1000d / TickRate);

        // TODO track if code blocks have changed
        private static bool codeBlocksChanged = false;

        // TODO track if gear has changed
        private static bool gearChanged = true;

        static private System.Diagnostics.Stopwatch Watch = 
            System.Diagnostics.Stopwatch.StartNew();

        /// <summary>
        /// Callback executed on each game tick.
        /// </summary>
        /// <param name="_">game object that is passed by the timer</param>
        private void GameTick(double delta)
        {
            var game = this;

            if (!game.enemySpawnLimiter.IsCooldownActive()) {
                game.SpawnEnemy(Design.SpawnRange);
            }
            game.UpdateStateWithControls(delta);
            
            foreach (Hero h in game.heroes)
            {
                h.Move(delta);
                h.Algorithm.Execute(game.GameState);
            }

            foreach (Enemy enemy in game.enemies) {
                enemy.VelocityTowardsClosestEntity(game.heroes);
                enemy.Move(delta);
                if (enemy is Shooter shooter)
                    game.TryAddEnemyProjectile(shooter);
            }

            foreach (Hero h in game.heroes)
            {
                foreach (Projectile projectile in h.Projectiles)
                {
                    projectile.Move(delta);
                }
            }

            game.CheckForCollisionsAndUpdateGame();
            game.RemoveProjectilesOutsideOfMap();

            HubContextExtensions.SendUpdatesToClients(game.hubContext, game.GameState);
            foreach (Hero h in game.heroes)
            {
                if (h.Points >= 40)
                {
                    if (gearChanged)
                    {
                        gearChanged = false;
                        h.AddGear(Design.GearDict["hoverboard"]);
                        h.Points -= 40;
                    }
                }
            }
            if (codeBlocksChanged) {
                codeBlocksChanged = false;
                HubContextExtensions.SendCodeUpdate(game.hubContext, game.GameState.Hero.Algorithm);
            }
        }

        private static void GameLoop(object g)
        {
            var game = g as Game;

            while (true)
            {
                var ExecWatch = System.Diagnostics.Stopwatch.StartNew();
                Watch.Stop();
                var mss = Watch.ElapsedMilliseconds;
                Watch = System.Diagnostics.Stopwatch.StartNew();
                double delta = mss / RefreshTimeSpan.TotalMilliseconds;

                game.GameTick(delta);
                ExecWatch.Stop();
                var ms = ExecWatch.ElapsedMilliseconds;
                Thread.Sleep(RefreshTimeSpan);
                Log.Debug($"exec ms = {ms}, between calls = {mss}, delta = {delta}");
            }
        }

        public GameState GameState => new GameState(this.heroes, this.enemies, /* TODO */ null);

        public IReadOnlyCollection<Projectile> EnemyProjectiles => this.enemyProjectiles;

        private readonly ConcurrentHashSet<Projectile> enemyProjectiles = new ConcurrentHashSet<Projectile>();
        private readonly ConcurrentHashSet<Hero> heroes;
        private readonly ConcurrentHashSet<Enemy> enemies;  // TODO doesnt have to be concurrent
        private readonly Timer ticker;
        private readonly IHubContext<GameHub> hubContext;
        private readonly RateLimiter enemySpawnLimiter;

        /// <summary>
        /// Constructs a new Game which uses a HubContext interface to send data to clients.
        /// </summary>
        /// <param name="hctx"></param>
        public Game(IHubContext<GameHub> hctx)
        {
            this.heroes = new ConcurrentHashSet<Hero>();
            Hero playerOne = new Hero(1, Design.HeroStartX, Design.HeroStartY, 
                new Equipment.CodeStorage(), new Equipment.EquipmentStorage()
            );

            var shootElementList = new List<BaseCodeElement> {
                new TargetElement(),
                new ShootElement(new RateLimiter(Design.ShootElementCooldown)),
                new ShootElement(new RateLimiter(Design.ShootElementCooldown))
            };

            var idleElementList = new List<BaseCodeElement> {
                new IdleElement(),
                new IdleElement(),
                new IdleElement(),
            };

            var branchingElement = new BranchingElement(
                new IsEnemyNearCondition(),
                new CodeBlockElement(shootElementList),
                new CodeBlockElement(idleElementList)
            );

            playerOne.Algorithm.Insert(branchingElement);
            playerOne.Algorithm.Insert(new IdleElement());
            playerOne.Algorithm.Insert(new ShootElement(
                new RateLimiter(Design.ShootElementCooldown)));

            this.heroes.Add(playerOne);

            this.enemies = new ConcurrentHashSet<Enemy>();
            this.SpawnEnemy(Design.SpawnRange);
            this.hubContext = hctx;
            this.enemySpawnLimiter = new RateLimiter(Design.SpawnInterval);
            this.ticker = new Timer(GameLoop, this, 0, Timeout.Infinite);
        }

        /// <summary>
        /// Spawns an enemy in the given square radius around the hero.
        /// </summary>
        /// <param name="inRange"></param>
        public void SpawnEnemy(double inRange)
        {
            // TODO make this to work nicely whith multiplayer - create only one enemy
            foreach (Hero h in this.heroes)
            {
                this.enemies.Add(Enemy.Spawn<Kamikaze>(h.Xpos, h.Ypos, inRange));
            }
        }

        /// <summary>
        /// Updates the current hero controls based on the update received from the frontend.
        /// </summary>
        /// <param name="update"></param>
        public void UpdateControls(GameStateUpdate update)
        {
            foreach (Hero h in this.heroes)
            {
                foreach ((ConsoleKey key, bool isDown) in update.KeyPresses)
                    h.UpdateControls(key, isDown);
            }
        }

        /// <summary>
        /// Updates the hero movement based on the controls pressed/released.
        /// </summary>
        /// <param name="delta">relative delta time</param>
        public void UpdateStateWithControls(double delta)
        {
            foreach (Hero h in this.heroes)
            {
                h.UpdateStateWithControls(delta);
            }           
        }

        private void CheckForCollisionsAndUpdateGame()
        {
            this.CheckCollisionEnemyHero();
            this.CheckCollisionEnemyProjectile();
        }

        #region Helper functions

        private void CheckCollisionEnemyProjectile()
        {
            foreach (Hero h in this.heroes)
            {
                IReadOnlyList<Collision> collisions = CollisionCheck.GetCollisions(this.enemies, h.Projectiles);

                foreach (Collision c in collisions)
                {
                    Entity enemy = c.Collider1;
                    Entity projectile = c.Collider2;

                    enemy.DecreaseHealthPoints(projectile.Damage);

                    // Removing projectile and enemy (if it's dead)
                    if (enemy.Hp <= 0)
                    {
                        this.enemies.TryRemove((Enemy)enemy);
                        h.Points += (int)Math.Ceiling(enemy.Damage);
                    }

                    h.TryRemoveProjectile((Projectile)projectile);
                }
            }
        }

        private void CheckCollisionEnemyHero()
        {
            foreach (Hero h in this.heroes)
            {
                var heroList = new List<Entity> { h };
                IReadOnlyList<Collision> collisionsWithHero = CollisionCheck.GetCollisions(heroList, this.enemies);

                foreach (Collision c in collisionsWithHero) {
                    Entity hero = c.Collider1;
                    Entity kamikaze = c.Collider2;
                    kamikaze.DecreaseHealthPoints(hero.Damage);

                    // Remove kamikaze from the game
                    this.enemies.TryRemove((Enemy)kamikaze);

                    h.DecreaseHealthPoints(kamikaze.Damage);
                } }
        }

        private void RemoveProjectilesOutsideOfMap()
        {
            foreach (Hero h in this.heroes)
            {
                IReadOnlyCollection<Projectile> projectiles = h.Projectiles;
                foreach (Projectile p in projectiles)
                {
                    if (p.IsOutsideBounds(Design.MapSize))
                    {
                        if (h.TryRemoveProjectile(p))
                        {
                            //Log.Debug($"Removed projectile on location: " +
                            //    $"({ p.Xpos}, { p.Ypos}) because it's outside of the map.");
                        }
                        else
                        {
                            Log.Debug($"Failed to remove projectile on location:" +
                                $" ({p.Xpos}, {p.Ypos}) because it's outside of the map.");
                        }
                    }
                }
            }

            foreach (Projectile p in this.EnemyProjectiles) {
                if (p.IsOutsideBounds(Design.MapSize)) {
                    if (this.TryRemoveEnemyProjectile(p)) {
                        //Log.Debug($"Removed projectile on location: " +
                        //    $"({ p.Xpos}, { p.Ypos}) because it's outside of the map.");
                    } else {
                        Log.Debug($"Failed to remove projectile on location:" +
                            $" ({p.Xpos}, {p.Ypos}) because it's outside of the map.");
                    }
                }
            }
        }

        public bool TryAddEnemyProjectile(Shooter shooter)
        {
            Projectile p = shooter.TryFire();
            return p != null && this.enemyProjectiles.Add(p);
        }

        public bool TryRemoveEnemyProjectile(Projectile p)
            =>  this.enemyProjectiles.TryRemove(p);

        #endregion
    }
}
