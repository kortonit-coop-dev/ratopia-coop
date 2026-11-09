namespace RatopiaUnitBrain.Data
{
    public enum BrainControlMode : byte
    {
        /// <summary>
        /// Local vanilla AI executes Check_AllDesire() and UpdateFunction(). Standard for Host or singleplayer.
        /// </summary>
        Autonomous = 0,

        /// <summary>
        /// UpdateFunction() is suppressed. Unit is controlled externally via BrainPuppeteer based on network commands.
        /// Native physics (falling, stairs, minecarts, lifts) and coroutines still run smoothly.
        /// </summary>
        RemotePuppet = 1,

        /// <summary>
        /// Controlled via custom mod scripts or cinematic sequences.
        /// </summary>
        Scripted = 2
    }
}
