# vrc-avatar-controller-cleaner
[![Discord](https://img.shields.io/discord/1351240463096615042?style=for-the-badge&logo=discord&logoColor=white&label=Discord&labelColor=%235865F2&color=%23323232)](https://discord.gg/rcCCkbDsY3)
[![VirusTotal](https://img.shields.io/badge/-VirusTotal-394EFF?style=for-the-badge&logo=virustotal&link=https%3A%2F%2Fwww.virustotal.com%2Fgui%2Ffile%2F7d7cc304ea77622f58b36d308d5904585e04141a97b0b231e402308aaa1234ce)](https://www.virustotal.com/gui/file/7d7cc304ea77622f58b36d308d5904585e04141a97b0b231e402308aaa1234ce)

A Unity tool that can remove unused parameters as well as dead code that exists in your avatars FX Controller, generating a new cleaned FX controller for you and leaving the original FX controller untouched

If you have ever seen the Unity Error: `Local file identifier (xxxxxxxx) doesn't exist!`, this tool can usually fix that for you

## Features

- Remove unused parameters from the controller
- Remove dead code Unity failed to clean up
- Preserve Gesture Weight parameters if desired
- Preview and confirm parameter removals before cleaning
- Automatically apply the cleaned FX Controller to the avatar
- Leaves the original FX Controller untouched

## Options

### Keep Gesture Weights

Don't remove Gesture Weight parameters even if they are not being used

### Remove Unused Parameters

Removes parameters that exist in the parameter list but are not used in the controller

#### Confirm Changes Before Removing

Shows a list of staged parameter removals and allows you to keep selected parameters before cleaning

### Remove Dead Code

Removes dead code from the controller that Unity did not remove

### Apply Cleaned Controller To Avatar

Will automatically apply the new cleaned FX Controller to the avatar

Your old FX controller will remain untouched

## Usage

- Open the tool from:
  - **Tools** -> **Nymh** -> **Avatar Controller Cleaner**
- Drag your avatar from the hierarchy into the Avatar input field
- The FX Controller field will automatically populate using the avatar descriptor
- Select the options you want
- Click **Clean**

It will then generate a `(avatarName).Cleaned.controller` that you can replace on your avatar descriptor.

Your original FX Controller will stay untouched

## Requirements

- [VRChat Creator Companion](https://vcc.docs.vrchat.com/#download-it) OR [ALCOM](https://vrc-get.anatawa12.com/en/alcom/)
- [Unity 2022.3.22f1](https://unity.com/releases/editor/whats-new/2022.3.22f1)
- An avatar project and any avatar with an FX Controller

## Importing

### VPM

Use the [VPM Listing](https://nymmh.github.io/nymh-vpm/) to add the repository to:

- [VRChat Creator Companion](https://vcc.docs.vrchat.com/#download-it)
- [ALCOM](https://vrc-get.anatawa12.com/en/alcom/)

Then click **Manage** and add **Avatar Controller Cleaner** to your project.

### Manual

Download it from either my GitHub repo or my Jinxxy store

- [Github](https://github.com/Nymmh/vrc-avatar-controller-cleaner/releases)
- [Jinxxy](https://jinxxy.com/Nymh)

Import the `vrc-avatar-controller-cleaner.dll` and `vrc-avatar-controller-cleaner.pdb` files into your Unity project.

## Removing

If you used the VPM remove the package using:

- [VRChat Creator Companion](https://vcc.docs.vrchat.com/#download-it)
- [ALCOM](https://vrc-get.anatawa12.com/en/alcom/)

If you manually imported it:

- Delete the `vrc-avatar-controller-cleaner.dll` and `vrc-avatar-controller-cleaner.pdb` files into your Unity project.

## FAQ

### Why am I still getting errors for `Local file identifier (xxxxxxxx) doesn't exist!`?

This usually happens from Unity cache

1. Clear the Unity console
2. Right-click the cleaned controller
3. Click **Reimport**

### Does this tool take into account VRC Fury prefabs on the avatar?

No

### Can I use this tool on FX Controllers from prefabs?

You can, however results might be unexpected

1. Put your avatar from the hierarchy into the Avatar input box
2. Replace the FX Controller with the prefab controller you wish to clean
3. Uncheck **Apply Cleaned Controller To Avatar**
4. Click **Clean**

### Where is the cleaned controller saved?

It is saved in the same folder as the original FX controller

### What happens if a cleaned controller already exists?

The existing cleaned controller will be replaced

### Why is the Clean button disabled?

It is disabled when both **Remove Unused Parameters** and **Remove Dead Code** are unchecked

### Why did I get "There was nothing to be cleaned"?

The tool found nothing to be cleaned
