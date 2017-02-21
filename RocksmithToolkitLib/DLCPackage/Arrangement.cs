﻿using System;
using System.Collections.Generic;
using System.Data;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;
using System.Xml.Linq;
using RocksmithToolkitLib.DLCPackage.AggregateGraph;
using RocksmithToolkitLib.DLCPackage.Manifest2014;
using RocksmithToolkitLib.Sng;
using RocksmithToolkitLib.Sng2014HSL;
using RocksmithToolkitLib.Xml;
using RocksmithToolkitLib.XmlRepository;

namespace RocksmithToolkitLib.DLCPackage
{
    public enum RouteMask : int
    {
        // Used for lessons or for display only in song list
        None = 0,
        Lead = 1,
        Rhythm = 2,
        Any = 3,
        Bass = 4
    }

    public enum DNAId : int
    {
        None = 0,
        Solo = 1,
        Riff = 2,
        Chord = 3
    }

    public class Arrangement
    {
        public SongFile SongFile { get; set; }
        public SongXML SongXml { get; set; }
        // Song Information
        public SongArrangementProperties2014 ArrangementPropeties { get; set; }
        public ArrangementType ArrangementType { get; set; }
        public int ArrangementSort { get; set; }
        public ArrangementName Name { get; set; }
        public string Tuning { get; set; } //tuning Name
        public TuningStrings TuningStrings { get; set; }
        public double TuningPitch { get; set; }
        public decimal CapoFret { get; set; }
        public int ScrollSpeed { get; set; }
        public PluckedType PluckedType { get; set; }
        public bool CustomFont { get; set; } //true for JVocals and custom fonts (planned)
        public string FontSng { get; set; }
        // cache parsing results (speeds up generating for multiple platforms)
        public Sng2014File Sng2014 { get; set; }
        // Gameplay Path
        public RouteMask RouteMask { get; set; }
        public bool BonusArr { get; set; }
        // Tone Selector
        public string ToneBase { get; set; }
        public string ToneMultiplayer { get; set; }
        public string ToneA { get; set; }
        public string ToneB { get; set; }
        public string ToneC { get; set; }
        public string ToneD { get; set; }
        // DLC ID
        public Guid Id { get; set; }
        public int MasterId { get; set; }
        // Metronome
        public Metronome Metronome { get; set; }
        // preserve EOF and DDC comments
        [IgnoreDataMember] // required for SaveTemplate feature to work
        public IEnumerable<XComment> XmlComments { get; set; }

        public Arrangement()
        {
            Id = IdGenerator.Guid();
            MasterId = (ArrangementType == ArrangementType.Vocal || ArrangementType == ArrangementType.ShowLight) ? 1 : RandomGenerator.NextInt();
        }

        /// <summary>
        /// Fill Arrangement 2014 from json and xml.
        /// </summary>
        /// <param name="attr"></param>
        /// <param name="xmlSongFile"></param>
        /// <param name="fixMultiTone">If set to <c>true</c> fix low bass tuning </param>
        /// <param name="fixLowBass">If set to <c>true</c> fix multitone exceptions </param>
        public Arrangement(Attributes2014 attr, string xmlSongFile, bool fixMultiTone = false, bool fixLowBass = false)
        {
            var song = Song2014.LoadFromFile(xmlSongFile);

            this.SongFile = new SongFile { File = "" };
            this.SongXml = new SongXML { File = xmlSongFile };

            //Properties
            Debug.Assert(attr.ArrangementType != null, "Missing information from manifest (ArrangementType)");
            SetArrType(attr.ArrangementType);

            this.ArrangementPropeties = attr.ArrangementProperties;
            this.ArrangementSort = attr.ArrangementSort;
            this.Name = (ArrangementName)Enum.Parse(typeof(ArrangementName), attr.ArrangementName);
            this.ScrollSpeed = Convert.ToInt32(attr.DynamicVisualDensity.Last() * 10);
            this.PluckedType = (PluckedType)attr.ArrangementProperties.BassPick;
            this.RouteMask = (RouteMask)attr.ArrangementProperties.RouteMask;
            this.BonusArr = attr.ArrangementProperties.BonusArr == 1;
            this.Metronome = (Metronome)attr.ArrangementProperties.Metronome;
            this.ToneMultiplayer = attr.Tone_Multiplayer;
            this.Id = Guid.Parse(attr.PersistentID);
            this.MasterId = attr.MasterID_RDV;

            //Filter out showlights\vocals
            if (ArrangementType != ArrangementType.Guitar && ArrangementType != ArrangementType.Bass)
                return;

            // save xml comments
            this.XmlComments = Song2014.ReadXmlComments(xmlSongFile);

            //Tones
            if (attr.Tones == null) // RS2012
            {
                this.ToneBase = attr.Tone_Base;

                if (attr.Tone_A != null || attr.Tone_B != null || attr.Tone_C != null || attr.Tone_D != null)
                    throw new DataException("RS2012 CDLC has extraneous tone data.");
            }
            else // RS2014 or Converter RS2012
            {
                // TODO: optimize using common Arrangment.cs method
                // verify the xml Tone_ exists in tone.manifest.json
                foreach (var jsonTone in attr.Tones)
                {
                    if (jsonTone == null)
                        continue;

                    // fix for tone.id (may not be needed/used by game)
                    Int32 toneId = 0;

                    // cleanup the xml arrangement file too
                    if (jsonTone.Name.ToLower() == attr.Tone_Base.ToLower())
                        this.ToneBase = song.ToneBase = attr.Tone_Base;
                    if (attr.Tone_A != null && jsonTone.Name.ToLower() == attr.Tone_A.ToLower())
                        this.ToneA = song.ToneA = attr.Tone_A;
                    if (attr.Tone_B != null && jsonTone.Name.ToLower() == attr.Tone_B.ToLower())
                    {
                        this.ToneB = song.ToneB = attr.Tone_B;
                        toneId = 1;
                    }
                    if (attr.Tone_C != null && jsonTone.Name.ToLower() == attr.Tone_C.ToLower())
                    {
                        this.ToneC = song.ToneC = attr.Tone_C;
                        toneId = 2;
                    }
                    if (attr.Tone_D != null && jsonTone.Name.ToLower() == attr.Tone_D.ToLower())
                    {
                        this.ToneD = song.ToneD = attr.Tone_D;
                        toneId = 3;
                    }

                    // update EOF tone name and set tone id
                    if (song.Tones != null)
                        foreach (var xmlTone in song.Tones)
                        {
                            // fix some old toolkit behavior
                            if (xmlTone.Name == "ToneA")
                                xmlTone.Name = attr.Tone_A;
                            if (xmlTone.Name == "ToneB")
                                xmlTone.Name = attr.Tone_B;
                            if (xmlTone.Name == "ToneC")
                                xmlTone.Name = attr.Tone_C;
                            if (xmlTone.Name == "ToneD")
                                xmlTone.Name = attr.Tone_D;

                            if (xmlTone.Name.ToLower() == jsonTone.Name.ToLower() || jsonTone.Name.ToLower().EndsWith(xmlTone.Name.ToLower())) //todo: SAMENAME tone fix?
                            {
                                xmlTone.Name = jsonTone.Name;
                                xmlTone.Id = toneId;
                            }
                        }


                    // song.Tones => id, name, time to apply tone is missing when song.Tones == null
                    if (song.Tones == null && toneId > 0)
                    {
                        // convert the corrupt multitone to a single tone instead of throwing exception
                        if (fixMultiTone)
                            song.Tones = new SongTone2014[0]; // => song.Tones.Length == 0
                        else
                            throw new InvalidDataException("Tone data is missing in CDLC and multitones will not change properly in game." + Environment.NewLine + "Please re-author XML arrangements in EOF and repair multitones name and time changes.");
                    }
                }

                // convert corrupt multitone to single tone and/or cleanup/repair old toolkit single tone
                // ToneA in single tone ODLC is null/empty
                if ((song.Tones == null || song.Tones.Length == 0) && !String.IsNullOrEmpty(song.ToneA))
                {
                    song.ToneA = song.ToneB = song.ToneC = String.Empty;
                    song.ToneBase = attr.Tone_Base;
                    this.ToneBase = attr.Tone_Base;
                    this.ToneA = this.ToneB = this.ToneC = String.Empty;
                }

                // set to standard tuning if no tuning exists
                if (song.Tuning == null)
                    song.Tuning = new TuningStrings { String0 = 0, String1 = 0, String2 = 0, String3 = 0, String4 = 0, String5 = 0 };

                this.TuningStrings = song.Tuning;

                // write changes to xml arrangement (w/o comments)
                using (var stream = File.Open(xmlSongFile, FileMode.Create))
                    song.Serialize(stream, true);

                // write comments back to xml now so they are available for debugging (used for Guitar and Bass)
                Song2014.WriteXmlComments(xmlSongFile, XmlComments, writeNewVers: false);

                // do a quick check/repair of low bass tuning, only for RS2014 bass arrangements
                if (fixLowBass && song.Version == "7" && this.ArrangementType == ArrangementType.Bass)
                    if (attr.Tuning.String0 < -4 && attr.CentOffset != -1200.0)
                        if (TuningFrequency.ApplyBassFix(this, fixLowBass))
                        {
                            attr.CentOffset = -1200.0; // Force 220Hz
                            song.Tuning = Song2014.LoadFromFile(xmlSongFile).Tuning;
                        }
            }

            // Set Final Tuning
            DetectTuning(song);
            this.CapoFret = attr.CapoFret;
            if (attr.CentOffset != null)
                this.TuningPitch = attr.CentOffset.Cents2Frequency();
        }

        /// <summary>
        /// Set Arrangement Type and Tuning information.
        /// </summary>
        /// <param name="song"></param>
        private void DetectTuning(Song2014 song)
        {
            var t = TuningDefinitionRepository.Instance.Detect(song.Tuning, GameVersion.RS2014, ArrangementType == ArrangementType.Guitar);
            Tuning = t.UIName;
            TuningStrings = t.Tuning;
        }

        /// <summary>
        /// Sets Arrangement Type
        /// </summary>
        /// <param name="ArrNameType"></param>
        private void SetArrType(int? arrNameType)
        {
            switch ((ArrangementName)arrNameType)
            {
                case ArrangementName.Bass:
                    ArrangementType = ArrangementType.Bass;
                    break;
                case ArrangementName.JVocals:
                case ArrangementName.Vocals:
                    ArrangementType = ArrangementType.Vocal;
                    break;
                case ArrangementName.ShowLights:
                    ArrangementType = ArrangementType.ShowLight;
                    break;
                default:
                    ArrangementType = ArrangementType.Guitar;
                    break;
            }
        }

        public override string ToString()
        {
            var toneDesc = String.Empty;
            if (!String.IsNullOrEmpty(ToneBase))
                toneDesc = ToneBase;
            // do not initially display duplicate ToneA in Arrangements listbox
            if (!String.IsNullOrEmpty(ToneA) && ToneBase != ToneA)
                toneDesc += String.Format(", {0}", ToneA);
            if (!String.IsNullOrEmpty(ToneB))
                toneDesc += String.Format(", {0}", ToneB);
            if (!String.IsNullOrEmpty(ToneC))
                toneDesc += String.Format(", {0}", ToneC);
            if (!String.IsNullOrEmpty(ToneD))
                toneDesc += String.Format(", {0}", ToneD);

            var capoInfo = String.Empty;
            if (CapoFret > 0)
                capoInfo = String.Format(", Capo Fret {0}", CapoFret);

            var pitchInfo = String.Empty;
            if (TuningPitch > 0 && !TuningPitch.Equals(440.0))
                pitchInfo = String.Format(": A{0}", TuningPitch);

            var metDesc = String.Empty;
            if (Metronome == Metronome.Generate)
                metDesc = " +Metronome";

            switch (ArrangementType)
            {
                case ArrangementType.Bass:
                    return String.Format("{0} [{1}{2}{3}] ({4}){5}", ArrangementType, Tuning, pitchInfo, capoInfo, toneDesc, metDesc);
                case ArrangementType.Vocal:
                case ArrangementType.ShowLight:
                    return String.Format("{0}", Name);
                default:
                    return String.Format("{0} - {1} [{2}{3}{4}] ({5}){6}", ArrangementType, Name, Tuning, pitchInfo, capoInfo, toneDesc, metDesc);
            }
        }

        public void ClearCache()
        {
            Sng2014 = null;
        }

    }
}
