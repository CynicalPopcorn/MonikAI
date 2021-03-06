﻿using MonikAI.Parsers;
using MonikAI.Parsers.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using ResponseTuple =
    System.Tuple<System.Collections.Generic.List<MonikAI.Expression[]>, System.Func<bool>, System.TimeSpan,
        System.DateTime>;


namespace MonikAI.Behaviours
{
    public class IdleBehaviour : IBehaviour
    {
        private readonly CSVParser parser = new CSVParser();
        private readonly Random random = new Random();

        private readonly Dictionary<string[], ResponseTuple> responseTable =
            new Dictionary<string[], ResponseTuple>(new TriggerComparer());

        private readonly object toSayLock = new object();
        private Expression[] toSay;

        private DateTime lastIdleUsage = DateTime.Now;
        private TimeSpan idleTimeout = new TimeSpan(0, 0, 0);
        private Expression[] lastIdleDialogue;

        private List<KeyValuePair<string, (int, int)>> timeoutSpeeds = new List<KeyValuePair<string, (int, int)>>()
        {
            new KeyValuePair<string, (int, int)>("very short", (30, 120)),
            new KeyValuePair<string, (int, int)>("short", (60, 180)),
            new KeyValuePair<string, (int, int)>("regular", (120, 300)),
            new KeyValuePair<string, (int, int)>("long", (180, 480)),
            new KeyValuePair<string, (int, int)>("very long", (240, 600))
        };

        public void Init(MainWindow window)
        {
            //Get first random timeout
            SetTimeOut();

            try
            {
                // Parse the CSV file
                var csvFile = this.parser.GetData("idle_dialogue");
                this.PopulateResponseTable(this.parser.ParseData(csvFile));
            }
            catch (Exception ex)
            {
                MessageBox.Show(window,
                    "An error occured: " + ex.Message + "\r\n\r\n(Try running MonikAI as an administrator.)");
            }
        }

        private void SetTimeOut()
        {
            var speed = timeoutSpeeds.Where(x => x.Key == MonikaiSettings.Default.IdleWait.ToLower()).FirstOrDefault();
            idleTimeout = TimeSpan.FromSeconds(random.Next(speed.Value.Item1, speed.Value.Item2 + 1));
        }

        /// <summary>
        /// Call to select the idle.
        /// </summary>
        private void GetIdleChatter()
        {
            bool isSelected = false;
            while (!isSelected)
            {
                var selectedSample = this.responseTable.First().Value.Item1.Sample();

                if (selectedSample != lastIdleDialogue)
                {
                    lock (this.toSayLock)
                    {
                        this.toSay = selectedSample;
                    }

                    //Set this to be the last used idle dialogue
                    lastIdleDialogue = this.responseTable.First().Value.Item1.Sample();

                    //We've selected an item
                    isSelected = true;

                    //As all the idle dialogue is under a blank dictionary entry and the idle dialogue has its own timeout system
                    //There's no need to update the key with the last execution tiem like you would in all other behaviours
                }
            }        
        }

        public void Update(MainWindow window)
        {
            //Just return before doing anything if the idle setting is off
            if (MonikaiSettings.Default.IdleWait.ToLower() == "off") return;

            //Check if it's time to idle again
            if (DateTime.Now - idleTimeout > lastIdleUsage)
            {
                //Must roll a 1/20. This just adds an arbitrary extra step to add some random time to the idle wait (even if very little).
                if (random.Next(0, 20) == 0)
                {
                    //Set the last used time
                    lastIdleUsage = DateTime.Now;

                    //Select a random timeout from 2 to 4 minutes.
                    SetTimeOut();

                    //Select an idle dialogue
                    GetIdleChatter();
                }
            }

            lock (this.toSayLock)
            {
                if (this.toSay != null)
                {
                    window.Say(this.toSay);
                    this.toSay = null;
                }
            }
        }

        /// <summary>
        ///     Fills the response table with the currently selected character's triggers and responses from the csv.
        /// </summary>
        /// <param name="characterResponses">A list containing all of the triggers and responses of the current character.</param>
        private void PopulateResponseTable(List<DokiResponse> characterResponses)
        {
            foreach (var response in characterResponses)
            {
                // Convert triggers to array to use as a key for the dictionary
                var triggers = response.ResponseTriggers.Select(x => x.ToLower().Trim()).ToArray();

                // Add every response to the current trigger into a new array to use as a value in the dictionary
                var responseChain = new Expression[response.ResponseChain.Count];
                for (var chain = 0; chain < response.ResponseChain.Count; chain++)
                {
                    responseChain[chain] = response.ResponseChain[chain];
                }

                Func<bool> triggerFunc = () => true;

                /* NOTE:
                * This should probably be re-designed.
                * The quick solution to checking if an entry exists would be to just iterate through every pair and then check every trigger in each key
                * which is being done in the eventarrived method.
                * Alternatively, implementing a custom comparer can be done which is what I've done here (see TriggerComparer.cs)
                * I think it would be better to just have an individual process name as a key because duplicating values is more performant than duplicating keys
                * O(1) lookup time is one of the main strengths of using a dictionary in the first place but that is lost when storing an array as a key.
                * Also unrelated, but it might be better to just create a public dictionary that gets populated from the parser class so that this method can be moved to keep this class cleaner.
                */

                // If key already exists in the table, append the new response chain
                List<Expression[]> triggerResponses;
                if (this.responseTable.ContainsKey(triggers))
                {
                    triggerResponses = this.responseTable[triggers].Item1;
                    triggerResponses.Add(responseChain);
                }
                else
                {
                    triggerResponses = new List<Expression[]> { responseChain };
                }

                // If trigger is a browser, only respond if the user recently launched the browser
                this.responseTable[triggers] = new ResponseTuple(triggerResponses, triggerFunc,
                    TimeSpan.FromMinutes(5), DateTime.MinValue);
            }
        }
    }
}
