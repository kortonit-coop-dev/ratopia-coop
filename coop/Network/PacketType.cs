namespace RatopiaCoop.Network
{
    public enum PacketType : byte
    {
        // Connection & Handshake (0x01 - 0x0F)
        HandshakeRequest = 0x01,
        HandshakeResponse = 0x02,
        Disconnect = 0x03,
        Ping = 0x04,
        Pong = 0x05,

        // World Transfer & Join (0x10 - 0x1F)
        SaveTransferStart = 0x10,
        SaveTransferChunk = 0x11,
        SaveTransferComplete = 0x12,
        ClientReady = 0x13,

        // Player Queen Sync (0x20 - 0x2F)
        PlayerPosition = 0x20,
        PlayerAction = 0x21,
        PlayerWeaponChange = 0x22,
        PlayerDamage = 0x23,
        PlayerRevived = 0x24,
        PlayerHealthUpdate = 0x25,  // BUG FIX #4: Immediate HP sync
        SceneTransition = 0x26,      // BUG FIX #3: Position preservation during scene changes

        // Orders & Blueprints (0x30 - 0x3F)
        OrderDigArea = 0x30,
        OrderCancelArea = 0x31,
        OrderPlaceBlueprint = 0x32,
        OrderCancelBlueprint = 0x33,
        RequestMineTile = 0x34,      // Client manual mining completion request -> Host validates & destroys
        RequestDamageEntity = 0x35,  // Client combat hit -> Host validates & applies authoritative damage

        // World & Tiles (0x40 - 0x4F)
        TileDestroyed = 0x40,
        TileCreated = 0x41,
        BuildingCompleted = 0x42,
        BuildingDemolished = 0x43,
        UnitPathCommit = 0x44,
        UnitAction = 0x45,
        UnitSpeechBubble = 0x46,
        AnimalSync = 0x47,
        WorldObjectUpdate = 0x48,     // Single plant/tree level or destroyed state sync
        WorldObjectBatchSync = 0x49,  // Full periodic snapshot of all world objects
        RequestHarvestWorldObj = 0x4A,// Client Queen interaction -> Host authoritatively harvests

        // Simulation & Economy Sync (0x50 - 0x5F)
        TimeSync = 0x50,
        CitizenSync = 0x51,
        EconomySync = 0x52,
        ResearchUnlock = 0x53,
        GamePause = 0x54,
        PauseAck = 0x56,             // BUG FIX #8: Pause acknowledgment from client
        InvasionAlert = 0x57,        // Synchronized invasion banner/audio

        // Social / Chat (0x60 - 0x6F)
        ChatMessage = 0x60
    }

    public enum PlayerActionType : byte
    {
        Jump = 0,
        SwordAttack = 1,
        BowAttack = 2,
        Skill = 3,
        Interact = 4,
        Revive = 5
    }

    public enum DamageTargetKind : byte
    {
        Unit = 0,      // GameUnit (GameEnemy, T_Citizen, GBot, etc.)
        Animal = 1,    // AnimalBody
        Building = 2,  // Building (EnemyNexus, HellAnvil, etc.)
        MapObj = 3     // MapObj (destructible map objects)
    }
}
