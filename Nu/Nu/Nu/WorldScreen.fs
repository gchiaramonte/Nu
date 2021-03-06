﻿// Nu Game Engine.
// Copyright (C) Bryan Edds, 2012-2016.

namespace Nu
open System
open System.Collections.Generic
open System.IO
open System.Reflection
open System.Runtime.CompilerServices
open OpenTK
open Prime
open Nu

[<AutoOpen>]
module WorldScreenModule =

    type Screen with

        member this.GetId world = World.getScreenId this world
        member this.GetName world = World.getScreenName this world
        member this.GetXtension world = World.getScreenXtension this world
        member this.GetDispatcherNp world = World.getScreenDispatcherNp this world
        member this.GetCreationTimeStampNp world = World.getScreenCreationTimeStampNp this world
        member this.GetOptSpecialization world = World.getScreenOptSpecialization this world
        member this.GetEntityTreeNp world = World.getScreenEntityTreeNp this world
        member this.SetEntityTreeNp value world = World.setScreenEntityTreeNp value this world
        member this.GetTransitionStateNp world = World.getScreenTransitionStateNp this world
        member this.SetTransitionStateNp value world = World.setScreenTransitionStateNp value this world
        member this.GetTransitionTicksNp world = World.getScreenTransitionTicksNp this world
        member this.SetTransitionTicksNp value world = World.setScreenTransitionTicksNp value this world
        member this.GetIncoming world = World.getScreenIncoming this world
        member this.SetIncoming value world = World.setScreenIncoming value this world
        member this.GetOutgoing world = World.getScreenOutgoing this world
        member this.SetOutgoing value world = World.setScreenOutgoing value this world
        member this.GetPersistent world = World.getScreenPersistent this world
        member this.SetPersistent value world = World.setScreenPersistent value this world

        /// Get a property value and type.
        member this.GetProperty propertyName world = World.getScreenProperty propertyName this world

        /// Get a property value.
        member this.Get propertyName world : 'a = World.getScreenPropertyValue propertyName this world

        /// Set a property value.
        member this.Set propertyName (value : 'a) world = World.setScreenPropertyValue propertyName value this world

        /// Check that a screen is in an idling state (not transitioning in nor out).
        member this.IsIdling world = this.GetTransitionStateNp world = IdlingState

        /// Check that a screen dispatches in the same manner as the dispatcher with the target type.
        member this.DispatchesAs (dispatcherTargetType : Type) world = Reflection.dispatchesAs dispatcherTargetType (this.GetDispatcherNp world)

    type World with

        static member private removeScreen screen world =
            let removeGroups screen world =
                let groups = World.proxyGroups screen world
                World.destroyGroupsImmediate groups world
            World.removeScreen3 removeGroups screen world

        static member internal updateScreen (screen : Screen) world =
            let dispatcher = World.getScreenDispatcherNp screen world
            let world = dispatcher.Update (screen, world)
            let eventTrace = EventTrace.record "World" "updateScreen" EventTrace.empty
            World.publish7 World.getSubscriptionsSorted World.sortSubscriptionsByHierarchy () (Events.Update ->- screen) eventTrace Simulants.Game world

        static member internal actualizeScreen (screen : Screen) world =
            let dispatcher = screen.GetDispatcherNp world
            dispatcher.Actualize (screen, world)

        /// Get all the world's screens.
        static member proxyScreens world =
            Vmap.fold
                (fun state _ (screenAddress, _) -> Screen.proxy screenAddress :: state)
                [] (World.getScreenDirectory world) :> _ seq

        /// Destroy a screen in the world immediately. Can be dangerous if existing in-flight publishing depends on the
        /// screen's existence. Consider using World.destroyScreen instead.
        static member destroyScreenImmediate screen world =
            World.removeScreen screen world

        /// Destroy a screen in the world on the next tick. Use this rather than destroyScreenImmediate unless you need
        /// the latter's specific behavior.
        static member destroyScreen screen world =
            let tasklet =
                { ScheduledTime = World.getTickTime world
                  Command = { Execute = fun world -> World.destroyScreenImmediate screen world }}
            World.addTasklet tasklet world

        /// Create a screen and add it to the world.
        static member createScreen dispatcherName optSpecialization optName world =
            let dispatchers = World.getScreenDispatchers world
            let dispatcher = Map.find dispatcherName dispatchers
            let screenState = ScreenState.make optSpecialization optName dispatcher
            let screenState = Reflection.attachProperties ScreenState.copy dispatcher screenState
            let screen = ntos screenState.Name
            let world = World.addScreen false screenState screen world
            (screen, world)
        
        /// Create a screen with a dissolving transition, and add it to the world.
        static member createDissolveScreen dissolveData dispatcherName optSpecialization optName world =
            let optDissolveImage = Some dissolveData.DissolveImage
            let (screen, world) = World.createScreen dispatcherName optSpecialization optName world
            let world = screen.SetIncoming { Transition.make Incoming with TransitionLifetime = dissolveData.IncomingTime; OptDissolveImage = optDissolveImage } world
            let world = screen.SetOutgoing { Transition.make Outgoing with TransitionLifetime = dissolveData.OutgoingTime; OptDissolveImage = optDissolveImage } world
            (screen, world)

        /// Write a screen to a screen descriptor.
        static member writeScreen screen screenDescriptor world =
            let writeGroups screen screenDescriptor world =
                let groups = World.proxyGroups screen world
                World.writeGroups groups screenDescriptor world
            World.writeScreen4 writeGroups screen screenDescriptor world

        /// Write multiple screens to a game descriptor.
        static member writeScreens screens gameDescriptor world =
            screens |>
            Seq.sortBy (fun (screen : Screen) -> screen.GetCreationTimeStampNp world) |>
            Seq.filter (fun (screen : Screen) -> screen.GetPersistent world) |>
            Seq.fold (fun screenDescriptors screen -> World.writeScreen screen ScreenDescriptor.empty world :: screenDescriptors) gameDescriptor.Screens |>
            fun screenDescriptors -> { gameDescriptor with Screens = screenDescriptors }

        /// Write a screen to a file.
        static member writeScreenToFile (filePath : string) screen world =
            let filePathTmp = filePath + ".tmp"
            let screenDescriptor = World.writeScreen screen ScreenDescriptor.empty world
            let screenDescriptorStr = scstring screenDescriptor
            let screenDescriptorPretty = Symbol.prettyPrint String.Empty screenDescriptorStr
            File.WriteAllText (filePathTmp, screenDescriptorPretty)
            File.Delete filePath
            File.Move (filePathTmp, filePath)

        /// Read a screen from a screen descriptor.
        static member readScreen screenDescriptor optName world =
            World.readScreen4 World.readGroups screenDescriptor optName world

        /// Read a screen from a file.
        static member readScreenFromFile (filePath : string) optName world =
            let screenDescriptorStr = File.ReadAllText filePath
            let screenDescriptor = scvalue<ScreenDescriptor> screenDescriptorStr
            World.readScreen screenDescriptor optName world

        /// Read multiple screens from a game descriptor.
        static member readScreens gameDescriptor world =
            Seq.foldBack
                (fun screenDescriptor (screens, world) ->
                    let (screen, world) = World.readScreen screenDescriptor None world
                    (screen :: screens, world))
                gameDescriptor.Screens
                ([], world)

namespace Debug
open Prime
open Nu
open System.Reflection
open System.Collections.Generic
type Screen =

    /// Provides a view of all the member properties of a screen. Useful for debugging such as with
    /// the Watch feature in Visual Studio.
    static member viewMemberProperties screen world = World.viewScreenMemberProperties screen world

    /// Provides a view of all the xtension properties of a screen. Useful for debugging such as
    /// with the Watch feature in Visual Studio.
    static member viewXProperties screen world = World.viewScreenXProperties screen world

    /// Provides a full view of all the member values of a screen. Useful for debugging such
    /// as with the Watch feature in Visual Studio.
    static member view screen world = World.viewScreen screen world