'
' Copyright (C) 2013 getMaNGOS <http://www.getMangos.co.uk>
'
' This program is free software; you can redistribute it and/or modify
' it under the terms of the GNU General Public License as published by
' the Free Software Foundation; either version 2 of the License, or
' (at your option) any later version.
'
' This program is distributed in the hope that it will be useful,
' but WITHOUT ANY WARRANTY; without even the implied warranty of
' MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
' GNU General Public License for more details.
'
' You should have received a copy of the GNU General Public License
' along with this program; if not, write to the Free Software
' Foundation, Inc., 59 Temple Place, Suite 330, Boston, MA  02111-1307  USA
'

Imports System.Threading
Imports System.Net.Sockets
Imports System.Xml.Serialization
Imports System.IO
Imports System.Net
Imports System.Reflection
Imports System.Runtime.CompilerServices
Imports mangosVB.Common.BaseWriter
Imports mangosVB.Common


Public Module WC_Handlers_Group


    Public Sub On_CMSG_REQUEST_RAID_INFO(ByRef packet As PacketClass, ByRef Client As ClientClass)
        Log.WriteLine(LogType.DEBUG, "[{0}:{1}] CMSG_REQUEST_RAID_INFO", Client.IP, Client.Port)

        Dim q As New DataTable
        If Client.Character IsNot Nothing Then
            CharacterDatabase.Query(String.Format("SELECT * FROM characters_instances WHERE char_guid = {0};", Client.Character.GUID), q)
        End If

        Dim response As New PacketClass(OPCODES.SMSG_RAID_INSTANCE_INFO)
        response.AddInt32(q.Rows.Count)                                 'Instances Counts

        Dim i As Integer = 0
        For Each r As DataRow In q.Rows
            response.AddUInt32(r.Item("map"))                               'MapID
            response.AddUInt32(CInt(r.Item("expire")) - GetTimestamp(Now))  'TimeLeft
            response.AddUInt32(r.Item("instance"))                          'InstanceID
            'TODO: Is this is a counter, shouldn't it be counting ?
            response.AddUInt32(i)                                           'Counter
        Next
        Client.Send(response)
        response.Dispose()

    End Sub

    Public Enum PartyCommand As Byte
        PARTY_OP_INVITE = 0
        PARTY_OP_LEAVE = 2
    End Enum
    Public Enum PartyCommandResult As Byte
        INVITE_OK = 0                   'You have invited [name] to join your group.
        INVITE_NOT_FOUND = 1            'Cannot find [name].
        INVITE_NOT_IN_YOUR_PARTY = 2    '[name] is not in your party.
        INVITE_NOT_IN_YOUR_INSTANCE = 3 '[name] is not in your instance.
        INVITE_PARTY_FULL = 4           'Your party is full.
        INVITE_ALREADY_IN_GROUP = 5     '[name] is already in group.
        INVITE_NOT_IN_PARTY = 6         'You aren't in party.
        INVITE_NOT_LEADER = 7           'You are not the party leader.
        INVITE_NOT_SAME_SIDE = 8        'gms - Target is not part of your alliance.
        INVITE_IGNORED = 9              'Test is ignoring you.
        INVITE_RESTRICTED = 13
    End Enum
    Public Sub SendPartyResult(ByVal objCharacter As ClientClass, ByVal Name As String, ByVal operation As PartyCommand, ByVal result As PartyCommandResult)
        Dim response As New PacketClass(OPCODES.SMSG_PARTY_COMMAND_RESULT)
        response.AddInt32(operation)
        response.AddString(Name)
        response.AddInt32(result)
        objCharacter.Send(response)
        response.Dispose()
    End Sub

    Public Sub On_CMSG_GROUP_INVITE(ByRef packet As PacketClass, ByRef Client As ClientClass)
        If (packet.Data.Length - 1) < 6 Then Exit Sub
        packet.GetInt16()
        Dim Name As String = CapitalizeName(packet.GetString)

        Log.WriteLine(LogType.DEBUG, "[{0}:{1}] CMSG_GROUP_INVITE [{2}]", Client.IP, Client.Port, Name)

        Dim GUID As ULong = 0
        CHARACTERs_Lock.AcquireReaderLock(DEFAULT_LOCK_TIMEOUT)
        For Each Character As KeyValuePair(Of ULong, CharacterObject) In CHARACTERs
            If UCase(Character.Value.Name) = UCase(Name) Then
                GUID = Character.Value.GUID
                Exit For
            End If
        Next
        CHARACTERs_Lock.ReleaseReaderLock()


        Dim errCode As PartyCommandResult = PartyCommandResult.INVITE_OK
        'TODO: InBattlegrounds: INVITE_RESTRICTED
        If GUID = 0 Then
            errCode = PartyCommandResult.INVITE_NOT_FOUND
        ElseIf CHARACTERs(GUID).IsInWorld = False Then
            errCode = PartyCommandResult.INVITE_NOT_FOUND
        ElseIf GetCharacterSide(CHARACTERs(GUID).Race) <> GetCharacterSide(Client.Character.Race) Then
            errCode = PartyCommandResult.INVITE_NOT_SAME_SIDE
        ElseIf CHARACTERs(GUID).IsInGroup Then
            errCode = PartyCommandResult.INVITE_ALREADY_IN_GROUP
            Dim denied As New PacketClass(OPCODES.SMSG_GROUP_INVITE)
            denied.AddInt8(0)
            denied.AddString(Client.Character.Name)
            CHARACTERs(GUID).Client.Send(denied)
            denied.Dispose()
        ElseIf CHARACTERs(GUID).IgnoreList.Contains(Client.Character.GUID) Then
            errCode = PartyCommandResult.INVITE_IGNORED
        Else
            If Not Client.Character.IsInGroup Then
                Dim g As New Group(Client.Character)
                CHARACTERs(GUID).Group = Client.Character.Group
                CHARACTERs(GUID).GroupInvitedFlag = True
            Else
                If Client.Character.Group.IsFull Then
                    errCode = PartyCommandResult.INVITE_PARTY_FULL
                ElseIf Client.Character.IsGroupLeader = False AndAlso Client.Character.GroupAssistant = False Then
                    errCode = PartyCommandResult.INVITE_NOT_LEADER
                Else
                    CHARACTERs(GUID).Group = Client.Character.Group
                    CHARACTERs(GUID).GroupInvitedFlag = True
                End If
            End If

        End If

        SendPartyResult(Client, Name, PartyCommand.PARTY_OP_INVITE, errCode)

        If errCode = PartyCommandResult.INVITE_OK Then
            Dim invited As New PacketClass(OPCODES.SMSG_GROUP_INVITE)
            invited.AddInt8(1)
            invited.AddString(Client.Character.Name)
            CHARACTERs(GUID).Client.Send(invited)
            invited.Dispose()
        End If
    End Sub
    Public Sub On_CMSG_GROUP_CANCEL(ByRef packet As PacketClass, ByRef Client As ClientClass)
        Log.WriteLine(LogType.DEBUG, "[{0}:{1}] CMSG_GROUP_CANCEL", Client.IP, Client.Port)
    End Sub
    Public Sub On_CMSG_GROUP_ACCEPT(ByRef packet As PacketClass, ByRef Client As ClientClass)
        Log.WriteLine(LogType.DEBUG, "[{0}:{1}] CMSG_GROUP_ACCEPT", Client.IP, Client.Port)
        If Client.Character.GroupInvitedFlag AndAlso Not Client.Character.Group.IsFull Then
            Client.Character.Group.Join(Client.Character)
        Else
            SendPartyResult(Client, Client.Character.Name, PartyCommand.PARTY_OP_INVITE, PartyCommandResult.INVITE_PARTY_FULL)
            Client.Character.Group = Nothing
        End If

        Client.Character.GroupInvitedFlag = False
    End Sub
    Public Sub On_CMSG_GROUP_DECLINE(ByRef packet As PacketClass, ByRef Client As ClientClass)
        Log.WriteLine(LogType.DEBUG, "[{0}:{1}] CMSG_GROUP_DECLINE", Client.IP, Client.Port)
        If Client.Character.GroupInvitedFlag Then
            Dim response As New PacketClass(OPCODES.SMSG_GROUP_DECLINE)
            response.AddString(Client.Character.Name)
            Client.Character.Group.GetLeader.Client.Send(response)
            response.Dispose()

            Client.Character.Group.CheckMembers()
            Client.Character.Group = Nothing
            Client.Character.GroupInvitedFlag = False
        End If
    End Sub
    Public Sub On_CMSG_GROUP_DISBAND(ByRef packet As PacketClass, ByRef Client As ClientClass)
        Log.WriteLine(LogType.DEBUG, "[{0}:{1}] CMSG_GROUP_DISBAND", Client.IP, Client.Port)

        If Client.Character.IsInGroup Then
            'TODO: InBattlegrounds: INVITE_RESTRICTED
            If Client.Character.Group.GetMembersCount > 2 Then
                Client.Character.Group.Leave(Client.Character)
            Else
                Client.Character.Group.Dispose()
            End If
        End If
    End Sub
    Public Sub On_CMSG_GROUP_UNINVITE(ByRef packet As PacketClass, ByRef Client As ClientClass)
        If (packet.Data.Length - 1) < 6 Then Exit Sub
        packet.GetInt16()
        Dim Name As String = packet.GetString

        Log.WriteLine(LogType.DEBUG, "[{0}:{1}] CMSG_GROUP_UNINVITE [{2}]", Client.IP, Client.Port, Name)

        Dim GUID As ULong = 0
        CHARACTERs_Lock.AcquireReaderLock(DEFAULT_LOCK_TIMEOUT)
        For Each Character As KeyValuePair(Of ULong, CharacterObject) In CHARACTERs
            If UCase(Character.Value.Name) = UCase(Name) Then
                GUID = Character.Value.GUID
                Exit For
            End If
        Next
        CHARACTERs_Lock.ReleaseReaderLock()


        'TODO: InBattlegrounds: INVITE_RESTRICTED
        If GUID = 0 Then
            SendPartyResult(Client, Name, PartyCommand.PARTY_OP_LEAVE, PartyCommandResult.INVITE_NOT_FOUND)
        ElseIf Not Client.Character.IsGroupLeader Then
            SendPartyResult(Client, "", PartyCommand.PARTY_OP_LEAVE, PartyCommandResult.INVITE_NOT_LEADER)
        Else
            Client.Character.Group.Leave(CHARACTERs(GUID))
        End If

    End Sub
    Public Sub On_CMSG_GROUP_UNINVITE_GUID(ByRef packet As PacketClass, ByRef Client As ClientClass)
        If (packet.Data.Length - 1) < 13 Then Exit Sub
        packet.GetInt16()
        Dim GUID As ULong = packet.GetUInt64

        Log.WriteLine(LogType.DEBUG, "[{0}:{1}] CMSG_GROUP_UNINVITE_GUID [0x{2:X}]", Client.IP, Client.Port, GUID)

        'TODO: InBattlegrounds: INVITE_RESTRICTED
        If GUID = 0 Then
            SendPartyResult(Client, "", PartyCommand.PARTY_OP_LEAVE, PartyCommandResult.INVITE_NOT_FOUND)
        ElseIf CHARACTERs.ContainsKey(GUID) = False Then
            SendPartyResult(Client, "", PartyCommand.PARTY_OP_LEAVE, PartyCommandResult.INVITE_NOT_FOUND)
        ElseIf Not Client.Character.IsGroupLeader Then
            SendPartyResult(Client, "", PartyCommand.PARTY_OP_LEAVE, PartyCommandResult.INVITE_NOT_LEADER)
        Else
            Client.Character.Group.Leave(CHARACTERs(GUID))
        End If
    End Sub
    Public Sub On_CMSG_GROUP_SET_LEADER(ByRef packet As PacketClass, ByRef Client As ClientClass)
        If (packet.Data.Length - 1) < 6 Then Exit Sub
        packet.GetInt16()
        Dim Name As String = packet.GetString()

        Log.WriteLine(LogType.DEBUG, "[{0}:{1}] CMSG_GROUP_SET_LEADER [Name={2}]", Client.IP, Client.Port, Name)

        Dim GUID As ULong = GetCharacterGUIDByName(Name)
        If GUID = 0 Then
            SendPartyResult(Client, "", PartyCommand.PARTY_OP_INVITE, PartyCommandResult.INVITE_NOT_FOUND)
        ElseIf CHARACTERs.ContainsKey(GUID) = False Then
            SendPartyResult(Client, "", PartyCommand.PARTY_OP_INVITE, PartyCommandResult.INVITE_NOT_FOUND)
        ElseIf Not Client.Character.IsGroupLeader Then
            SendPartyResult(Client, Client.Character.Name, PartyCommand.PARTY_OP_INVITE, PartyCommandResult.INVITE_NOT_LEADER)
        Else
            Client.Character.Group.SetLeader(CHARACTERs(GUID))
        End If
    End Sub
    Public Sub On_CMSG_GROUP_RAID_CONVERT(ByRef packet As PacketClass, ByRef Client As ClientClass)
        Log.WriteLine(LogType.DEBUG, "[{0}:{1}] CMSG_GROUP_RAID_CONVERT", Client.IP, Client.Port)

        If Client.Character.IsInGroup Then
            SendPartyResult(Client, "", PartyCommand.PARTY_OP_INVITE, PartyCommandResult.INVITE_OK)

            Client.Character.Group.ConvertToRaid()
            Client.Character.Group.SendGroupList()

            WorldServer.GroupSendUpdate(Client.Character.Group.ID)
        End If
    End Sub
    Public Sub On_CMSG_GROUP_CHANGE_SUB_GROUP(ByRef packet As PacketClass, ByRef Client As ClientClass)
        If (packet.Data.Length - 1) < 6 Then Exit Sub
        packet.GetInt16()
        Dim name As String = packet.GetString
        If (packet.Data.Length - 1) < (6 + name.Length + 1) Then Exit Sub
        Dim subGroup As Byte = packet.GetInt8

        Log.WriteLine(LogType.DEBUG, "[{0}:{1}] CMSG_GROUP_CHANGE_SUB_GROUP [{2}:{3}]", Client.IP, Client.Port, name, subGroup)

        If Client.Character.IsInGroup Then
            Dim j As Integer
            
            For j = subGroup * GROUP_SUBGROUPSIZE To ((subGroup + 1) * GROUP_SUBGROUPSIZE - 1)
                If Client.Character.Group.Members(j) Is Nothing Then
                    Exit For
                End If
            Next

            For i As Integer = 0 To Client.Character.Group.Members.Length - 1
                If (Not Client.Character.Group.Members(i) Is Nothing) AndAlso Client.Character.Group.Members(i).Name = name Then
                    Client.Character.Group.Members(j) = Client.Character.Group.Members(i)
                    Client.Character.Group.Members(i) = Nothing
                    If Client.Character.Group.Leader = i Then Client.Character.Group.Leader = j
                    Client.Character.Group.SendGroupList()
                    Exit For
                End If
            Next
        End If
    End Sub
    Public Sub On_CMSG_GROUP_SWAP_SUB_GROUP(ByRef packet As PacketClass, ByRef Client As ClientClass)
        If (packet.Data.Length - 1) < 6 Then Exit Sub
        packet.GetInt16()
        Dim name1 As String = packet.GetString
        If (packet.Data.Length - 1) < (6 + name1.Length + 1) Then Exit Sub
        Dim name2 As String = packet.GetString

        Log.WriteLine(LogType.DEBUG, "[{0}:{1}] CMSG_GROUP_SWAP_SUB_GROUP [{2}:{3}]", Client.IP, Client.Port, name1, name2)

        If Client.Character.IsInGroup Then
            Dim j As Integer

            For j = 0 To Client.Character.Group.Members.Length - 1
                If (Not Client.Character.Group.Members(j) Is Nothing) AndAlso Client.Character.Group.Members(j).Name = name2 Then
                    Exit For
                End If
            Next

            For i As Integer = 0 To Client.Character.Group.Members.Length - 1
                If (Not Client.Character.Group.Members(i) Is Nothing) AndAlso Client.Character.Group.Members(i).Name = name1 Then
                    Dim tmpPlayer As CharacterObject = Client.Character.Group.Members(j)
                    Client.Character.Group.Members(j) = Client.Character.Group.Members(i)
                    Client.Character.Group.Members(i) = tmpPlayer
                    tmpPlayer = Nothing

                    If Client.Character.Group.Leader = i Then
                        Client.Character.Group.Leader = j
                    ElseIf Client.Character.Group.Leader = j Then
                        Client.Character.Group.Leader = i
                    End If

                    Client.Character.Group.SendGroupList()
                    Exit For
                End If
            Next
        End If
    End Sub
    Public Sub On_CMSG_LOOT_METHOD(ByRef packet As PacketClass, ByRef Client As ClientClass)
        If (packet.Data.Length - 1) < 21 Then Exit Sub
        packet.GetInt16()
        Dim Method As Integer = packet.GetInt32
        Dim Master As ULong = packet.GetUInt64
        Dim Threshold As Integer = packet.GetInt32

        Log.WriteLine(LogType.DEBUG, "[{0}:{1}] CMSG_LOOT_METHOD [Method={2}, Master=0x{3:X}, Threshold={4}]", Client.IP, Client.Port, Method, Master, Threshold)

        If Not Client.Character.IsGroupLeader Then
            Exit Sub
        End If

        Client.Character.Group.SetLootMaster(Master)
        Client.Character.Group.LootMethod = Method
        Client.Character.Group.LootThreshold = Threshold
        Client.Character.Group.SendGroupList()

        WorldServer.GroupSendUpdateLoot(Client.Character.Group.ID)
    End Sub

    Public Sub On_MSG_MINIMAP_PING(ByRef packet As PacketClass, ByRef Client As ClientClass)
        packet.GetInt16()
        Dim x As Single = packet.GetFloat
        Dim y As Single = packet.GetFloat

        Log.WriteLine(LogType.DEBUG, "[{0}:{1}] MSG_MINIMAP_PING [{2}:{3}]", Client.IP, Client.Port, x, y)

        If Client.Character.IsInGroup Then
            Dim response As New PacketClass(OPCODES.MSG_MINIMAP_PING)
            response.AddUInt64(Client.Character.GUID)
            response.AddSingle(x)
            response.AddSingle(y)
            Client.Character.Group.Broadcast(response)
            response.Dispose()
        End If

    End Sub
    Public Sub On_MSG_RANDOM_ROLL(ByRef packet As PacketClass, ByRef Client As ClientClass)
        If (packet.Data.Length - 1) < 13 Then Exit Sub
        packet.GetInt16()
        Dim minRoll As Integer = packet.GetInt32
        Dim maxRoll As Integer = packet.GetInt32

        Log.WriteLine(LogType.DEBUG, "[{0}:{1}] MSG_RANDOM_ROLL [min={2} max={3}]", Client.IP, Client.Port, minRoll, maxRoll)

        Dim response As New PacketClass(OPCODES.MSG_RANDOM_ROLL)
        response.AddInt32(minRoll)
        response.AddInt32(maxRoll)
        response.AddInt32(Rnd.Next(minRoll, maxRoll))
        response.AddUInt64(Client.Character.GUID)
        If Client.Character.IsInGroup Then
            Client.Character.Group.Broadcast(response)
        Else
            Client.SendMultiplyPackets(response)
        End If
        response.Dispose()
    End Sub
    Public Sub On_MSG_RAID_READY_CHECK(ByRef packet As PacketClass, ByRef Client As ClientClass)

        Log.WriteLine(LogType.DEBUG, "[{0}:{1}] MSG_RAID_READY_CHECK", Client.IP, Client.Port)

        If Client.Character.IsGroupLeader Then
            Client.Character.Group.BroadcastToOther(packet, Client.Character)
        Else
            If (packet.Data.Length - 1) < 6 Then Exit Sub
            packet.GetInt16()
            Dim result As Byte = packet.GetInt8

            If result = 0 Then
                'DONE: Not ready
                Client.Character.Group.GetLeader.Client.Send(packet)
            Else
                'DONE: Ready
                Dim response As New PacketClass(OPCODES.MSG_RAID_READY_CHECK)
                response.AddUInt64(Client.Character.GUID)
                Client.Character.Group.GetLeader.Client.Send(response)
                response.Dispose()
            End If
        End If
    End Sub
    Public Sub On_MSG_RAID_ICON_TARGET(ByRef packet As PacketClass, ByRef Client As ClientClass)
        If packet.Data.Length < 7 Then Exit Sub 'Too short packet
        If Client.Character.Group Is Nothing Then Exit Sub
        packet.GetInt16()
        Dim icon As Byte = packet.GetInt8()

        If icon = 255 Then
            'DONE: Send icon target list
            Dim response As New PacketClass(OPCODES.MSG_RAID_ICON_TARGET)
            response.AddInt8(1) 'Target list
            For i As Byte = 0 To 7
                If Client.Character.Group.TargetIcons(i) = 0 Then Continue For

                response.AddInt8(i)
                response.AddUInt64(Client.Character.Group.TargetIcons(i))
            Next
            Client.Send(response)
            response.Dispose()
        Else
            If icon > 7 Then Exit Sub 'Not a valid icon
            If packet.Data.Length < 15 Then Exit Sub 'Too short packet
            Dim GUID As ULong = packet.GetUInt64()

            'DONE: Set the raid icon target
            Client.Character.Group.TargetIcons(icon) = GUID

            Dim response As New PacketClass(OPCODES.MSG_RAID_ICON_TARGET)
            response.AddInt8(0) 'Set target
            response.AddInt8(icon)
            response.AddUInt64(GUID)
            Client.Character.Group.Broadcast(response)
            response.Dispose()
        End If
    End Sub

    Private Enum PromoteToMain As Byte
        MainTank = 0
        MainAssist = 1
    End Enum


    Public Sub On_CMSG_REQUEST_PARTY_MEMBER_STATS(ByRef packet As PacketClass, ByRef Client As ClientClass)
        If (packet.Data.Length - 1) < 13 Then Exit Sub
        packet.GetInt16()
        Dim GUID As ULong = packet.GetUInt64
        Log.WriteLine(LogType.DEBUG, "[{0}:{1}] CMSG_REQUEST_PARTY_MEMBER_STATS [{2:X}]", Client.IP, Client.Port, GUID)

        If Not CHARACTERs.ContainsKey(GUID) Then
            'Character is offline
            Dim response As PacketClass = BuildPartyMemberStatsOffline(GUID)
            Client.Send(response)
            response.Dispose()
        ElseIf CHARACTERs(GUID).IsInWorld = False Then
            'Character is offline (not in world)
            Dim response As PacketClass = BuildPartyMemberStatsOffline(GUID)
            Client.Send(response)
            response.Dispose()
        Else
            'Request information from WorldServer
            Dim response As New PacketClass(0)
            response.Data = CHARACTERs(GUID).GetWorld.GroupMemberStats(GUID, 0)
            Client.Send(response)
            response.Dispose()
        End If
    End Sub


End Module