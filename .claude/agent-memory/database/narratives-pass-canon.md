---
name: narratives-pass-canon
description: 2026-11 narratives deep dive: story webs added to existing bios/notes/headlines, who is tied to whom, what is text-only vs world-changing, rules for further passes
metadata:
  type: project
---

Branch db/narratives-deep-dive. Pattern: connective tissue goes INTO existing Bio/Note/Background text (text-only), told from BOTH sides; named people must be real authored people (NarrativesPassTests.BothWays + names list).

Webs (canon now): Belgrade basement club split by the war (Petrovic VMMA / Kovac IFA); Kazakh federation (Amanov legend; Batyr, Zhaksylyk, Batyrov); Romanian pair (Codrea VMMA / Toma IFA); Georgian supra table (Tsereteli, Marchenko, Kipiani); Buenos Aires strawweights on Sosa's mats (Fuentes AFC / Quiroga CK); Curitiba room (Vieira coaches Silva, Prado; Leal survived one Tuesday); Aco Paulista (Pires coaches Duarte); Ironside (Rooke: Brody wears his old bantam belt, O'Rourke trains there; Blackwood's old gym is below it and he wants rent); Harbour City (Solomona holds pads for Rahiri/Te Ariki/Rennie); Iron Range (Kroll: Hale, Okonjo; Tanner is the other "unwatchable" wrestler); Park vs Fujimoto junior judo final; owners: Ilic's gym was a tenant of Reuss's family federation (IFA parking-lot years), Hennessy's network carried the AFC's early TV until Ross took rights in-house, Bittencourt/Pires, Sekine/Arai, Rizzo fixes both Barone's and Weeks's cars; staff: Bakr was Pemberton's junior, Sorokina stitched Orlov, Blackwood/Rooke, Halvorsen coached Kroll.
Origins: Neves learned his first armbar at the Quintela bakery academy (Crucible's secret favourite owes the DLB house); Tsukamoto threw Thibodeau; Thanasarn/Yoon temple-fair boards; Baek writes to both sumo men; Skaggs/Quirke want one bracket; Pastor keeps faxing Sampaio for the garage; Hiyama owns a tape of a Rio garage night.

Rules learned: headlines/feud reasons/bond notes are scanned by StoriesBatch1Tests.NewProse with a claim regex (digits, number words, first/only/never/every/always/nobody, month names, "may"): keep them claim-free; "says only" trips it. Tests that pinned headline/bond positions from the END were rewritten to relative order. Files are CRLF: scripted edits must write \r\n. Pioneer bios must stay 2-6 punctuation marks.
World-changing commit: 7 bonds appended (Mentor Pires>Duarte, Vieira>Silva, Wongsawat>Saengkham, Rooke>Brody, Solomona>Rahiri; Friend Fuentes-Quiroga, Tsereteli-Marchenko). No RNG; runtime effects only (mentor dev bonus, friend matchmaking block + FA pull, derived mentor news).
