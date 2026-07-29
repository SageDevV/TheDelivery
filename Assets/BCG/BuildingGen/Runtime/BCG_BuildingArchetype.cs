//----------------------------------------------
//        BCG Building Generator
//
// Copyright 2026 BoneCracker Games
// https://www.bonecrackergames.com
// Ekrem Bugra Ozdoganlar
//----------------------------------------------

namespace BoneCrackerGames.BuildingGen {

    /// <summary>Structural preset. Tower = storefront ground + concrete parapet; Shop = storefront
    /// + dark fascia parapet (1-2 floors); Apartment = window bands on every floor; House = 1-2
    /// floor gabled residential (pitched shingle roof, front door, eaves, no parapet/clutter/massing).
    /// House is APPENDED LAST (= 3) so existing serialized enum ints stay stable.</summary>
    public enum BCG_BuildingArchetype { Tower, Shop, Apartment, House }

}
