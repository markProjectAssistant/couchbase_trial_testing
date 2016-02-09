using Android.App;
using Android.Widget;
using Android.OS;
using Android.Util;
using Couchbase.Lite;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.IO;

namespace Random.Droid
{
	[Activity (Label = "Random", MainLauncher = true, Icon = "@mipmap/icon")]
	public class MainActivity : Activity
	{
		const string DB_NAME = "sync_gateway";
		const string TAG = "CouchbaseEvents";
		Database db;

		string _docID;

		Replication _push, _pull;
		Button createDoc, retrieveDoc, addAttachment, retrieveAttachment, deleteDoc, execQuery;
		protected override void OnCreate (Bundle savedInstanceState)
		{
			Xamarin.Insights.Initialize (global::Random.Droid.XamarinInsights.ApiKey, this);
			base.OnCreate (savedInstanceState);
			// Set our view from the "main" layout resource
			SetContentView (Resource.Layout.Main);
			// Get our button from the layout resource,
			// and attach an event to it

//			Log.Debug (TAG, "Begin Couchbase Events App");
			HelloCBL ();
//			Log.Debug (TAG, "End Couchbase Events App");


			Button button = FindViewById<Button> (Resource.Id.startRep);
			button.Click += delegate {
				StartReplications();
			};

			createDoc = FindViewById<Button> (Resource.Id.createDoc);
			createDoc.Click += (sender, e) => {
				_docID = CreateDocument();
			};

			retrieveDoc = FindViewById<Button> (Resource.Id.retriveDoc);
			retrieveDoc.Click += (sender, e) => {
				RetrieveDocument(_docID);
			};

			addAttachment = FindViewById<Button> (Resource.Id.addAtt);
			addAttachment.Click += (sender, e) => {
				AddAttachment(_docID);
			};

			retrieveAttachment = FindViewById<Button> (Resource.Id.readAtt);
			retrieveAttachment.Click += (sender, e) => {
				ReadAttachment(_docID);
			};

			deleteDoc = FindViewById<Button> (Resource.Id.delDoc);
			deleteDoc.Click += (sender, e) => {
				DeleteDocument(_docID);
			};

			execQuery = FindViewById<Button> (Resource.Id.execQuery);
			execQuery.Click += (sender, e) => {
				var view = CreateEventsByDateView();
				LogQueryResultsAsync(view);
			};
		}

		void HelloCBL(){
			try{
				db = Manager.SharedInstance.GetDatabase(DB_NAME);
			}catch (Exception e){
				Log.Error (TAG, "Error getting database", e);
				return;
			}

//			var documentID = CreateDocument ();
//		  /*var retrievedDocument =*/RetrieveDocument (documentID);
//			UpdateDocument (documentID);
//			AddAttachment (documentID);
//			ReadAttachment (documentID);
//			DeleteDocument (documentID);

		}

		#region Create Document
		string CreateDocument(){
			var doc = db.CreateDocument ();
			string docId = doc.Id;
			var props = new Dictionary<string, object> {
				{ "name", "Big Party" },
				{ "location", "MyHouse" },
				{ "date", DateTime.Now }
			};
			try{
				doc.PutProperties(props);
				Log.Debug(TAG, string.Format("doc written to database with ID = {0}", doc.Id));
			}catch(Exception e){
				Log.Error (TAG, "Error putting properties to Couchbase Lite database", e);
			}
			return docId;
		}
		#endregion

		#region Retrieve Document
		Document RetrieveDocument (string docID){
			var retrieveDoc = db.GetDocument (docID);
			LogDocProperties (retrieveDoc);
			return retrieveDoc;
		}

		static void LogDocProperties(Document doc){
			doc.Properties.Select (x => string.Format ("key={0}, value={1}", x.Key, x.Value))
				.ToList ().ForEach (y => Log.Debug (TAG, y));
		}
		#endregion

		#region Update Document
		void UpdateDocument(string docID){
			var doc = db.GetDocument (docID);
			try{
				var updatedProps = new Dictionary<string, object>(doc.Properties);
				updatedProps.Add("eventDescription", "Everyone is envited!");
				updatedProps.Add("address", "123 Elm St.");
				doc.PutProperties(updatedProps);
				Log.Debug(TAG, "Updated Doc Properties");
				LogDocProperties(doc);
			}catch(CouchbaseLiteException e){
				Log.Error (TAG, "Error updating properties in Couchbase Lite database", e);
			}
		}
		#endregion

		#region Adding Attachment
		void AddAttachment(string docID){
			var doc = db.GetDocument (docID);
			try{
				var revision = doc.CurrentRevision.CreateRevision();
				var text = "This is some text in an attachment";
				var data = Encoding.ASCII.GetBytes(text);
				revision.SetAttachment(/*attachment name*/"binaryData", /*MIME (Multipurpose Internet Mail Extensions)*/"application/octet-stream", /*attachment data*/data);
				revision.Save();
			}catch(CouchbaseLiteException e){
				Log.Error (TAG, "Error saving attachment", e);
			}
		}
		#endregion

		#region Reading Attachment
		void ReadAttachment(string docID){
			var doc = db.GetExistingDocument (docID);
			var savedRev = doc.CurrentRevision;
			var attachment = savedRev.GetAttachment ("binaryData");
			using (var sr = new StreamReader (attachment.ContentStream)) {
				var data = sr.ReadToEnd ();
				Log.Debug (TAG, data);
			}
		}
		#endregion

		#region Delete Document
		void DeleteDocument(string docID){
			try{
				var doc = db.GetDocument(docID);
				doc.Delete();
				Log.Debug(TAG, string.Format("Deleted document deletion status = {0}", doc.Deleted));
			}catch(CouchbaseLiteException e){
				Log.Error (TAG, "Cannot delete document", e);
			}
		}
		#endregion

		#region Sync Gateway
		Uri CreateSyncUri(){
			Uri syncUri = null;
			string scheme = "http";
			string host = "blank";
			int port = 4984;
			string dbName = "blank";
			try{
				var uriBuilder = new UriBuilder(scheme, host, port, dbName);
				syncUri = uriBuilder.Uri;
			}catch(UriFormatException e){
				Log.Error (TAG, "Cannot create sync uri", e);
			}

			return syncUri;
		}

		void StartReplications ()
		{
			_pull = db.CreatePullReplication (CreateSyncUri ());
			_push = db.CreatePushReplication (CreateSyncUri ());
			_pull.Continuous = true;
			_push.Continuous = true;
			_pull.Start ();
			_push.Start ();        
		}
		#endregion

		#region Views
		Couchbase.Lite.View GetView (string name)
		{
			Couchbase.Lite.View cbView = null;
			try {
				cbView = db.GetView (name);
			} catch (CouchbaseLiteException e) {
				Console.WriteLine (e.StackTrace);
				Log.Error (TAG, "Cannot get view", e.StackTrace);
			}
			return cbView;
		}

		public Couchbase.Lite.View CreateEventsByDateView ()
		{
			var eventsByDateView = GetView ("eventsByDate");
			eventsByDateView.SetMap ((doc, emit) => emit ((string)doc ["date"], null), "1");
			return eventsByDateView;
		}

		async void LogQueryResultsAsync (Couchbase.Lite.View cbView)
		{
			var orderedQuery = cbView.CreateQuery ();
			orderedQuery.Descending = true;
			orderedQuery.Limit = 20;
			try {
				var results = await orderedQuery.RunAsync ();
				results.ToList ().ForEach (result => {
					var doc = result.Document;
					Log.Info (TAG, String.Format("Found document with id: {0}, Date = {1}", 
						result.DocumentId, doc.GetProperty<string>("date")));
				});
			} catch (CouchbaseLiteException e) {
				Log.Error (TAG, "Error querying view", e);
			}
			catch(Exception e){
				Log.Error (TAG, e.Message, e);
			}
		}
		#endregion
	}
}
