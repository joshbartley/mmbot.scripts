﻿/**
* <description>
*     Talks to JetBrains TeamCity CI server
* </description>
*
* <commands>
*     mmbot show me builds - Show status of currently running builds;
*     mmbot tc list projects - Show all available projects;
*     mmbot tc list buildTypes - Show all available build types;
*     mmbot tc list buildTypes of &lt;project&gt; - Show all available build types for the specified project;
*     mmbot tc list builds &lt;buildType&gt; &lt;number&gt; - Show the status of the last &lt;number&gt; builds.  Number defaults to five.;
*     mmbot tc list builds of &lt;buildType&gt; of &lt;project&gt; &lt;number&gt;- Show the status of the last &lt;number&gt; builds of the specified build type of the specified project. Number can only follow the last variable, so if project is not passed, number must follow buildType directly. &lt;number&gt; Defaults to 5;
*     mmbot tc build start &lt;buildType&gt; - Adds a build to the queue for the specified build type;
*     mmbot tc build start &lt;buildType&gt; of &lt;project&gt; - Adds a build to the queue for the specified build type of the specified project;
* </commands>
* 
* <configuration>
*     MMBOT_TEAMCITY_USERNAME -  The teamcity user name of an account with suitable access;
*     MMBOT_TEAMCITY_PASSWORD - The teamcity password;
*     MMBOT_TEAMCITY_HOSTNAME - The host name of the accessible TeamCity server e.g. build.mydomain.com;
*     MMBOT_TEAMCITY_SCHEME - (Optional) http | https - defaults to http;
* </configuration>
* 
* <author>
*     petegoo
* </author>
*/

using System.Text.RegularExpressions;

var robot = Require<Robot>();

private string _username;
private string _password;
private string _hostname;
private string _scheme;
private string _baseUrl;

private Dictionary<string, string> GetHeaders()
{
    return new Dictionary<string, string>
    {
        {"Authorization", string.Format("Basic {0}", Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(string.Format("{0}:{1}", _username, _password))))},
        {"Accept", "application/json"}
    };
}

_username = robot.GetConfigVariable("MMBOT_TEAMCITY_USERNAME");
_password = robot.GetConfigVariable("MMBOT_TEAMCITY_PASSWORD");
_hostname = robot.GetConfigVariable("MMBOT_TEAMCITY_HOSTNAME");
_scheme = robot.GetConfigVariable("MMBOT_TEAMCITY_SCHEME") ?? "http";
_baseUrl = string.Format("{0}://{1}", _scheme, _hostname);

if (_hostname != null)
{
    

    robot.Respond("show me builds$", msg => GetCurrentBuilds(robot, msg, null, (err, res, body) =>
    {
        if (err == null && (body["count"] == null || body["count"].Value<int>() == 0))
        {
            msg.Send("No builds are currently running");
            return;
        }

        CreateAndPublishBuildMap(body["build"], msg);
    }));

    robot.Respond(@"tc build start (.*)", msg =>
    {
        var configuration = msg.Match[1];
        var buildName = msg.Match[1];

        if (string.IsNullOrWhiteSpace(buildName))
        {
            msg.Send("Nothing to build. Specify a build");
            return;
        }
        string project = null;
        if(_buildTypeRegex.IsMatch(buildName))
        {

            var buildTypeMatches = _buildTypeRegex.Matches(buildName);
            configuration = buildTypeMatches[0].Groups[1].Value;
            project = buildTypeMatches[0].Groups[2].Value;
        }

        MapNameToIdForBuildType(robot, msg, configuration, project, (response, buildType) =>
        {
            if (buildType == null)
            {
                msg.Send(string.Format("Build type {0} was not found", buildName));
                return;
            }

            var url = string.Format("{0}/httpAuth/action.html?add2Queue={1}", _baseUrl,
                buildType["id"].Value<string>());

            msg.Http(url)
                .Headers(GetHeaders())
                .Get((err, res) =>
                {
                    if (err != null || res.StatusCode != HttpStatusCode.OK)
                    {
                        msg.Send("Fail! Something went wrong. Couldn't start the build for some reason");
                    }
                    else
                    {
                        msg.Send(
                            string.Format("Dropped a build in the queue for {0}. Run `tc list builds of {0}` to check the status", buildType["name"].Value<string>()));
                    }
                });
        });
                
    });

    robot.Respond(@"tc list (projects|buildTypes|builds) ?(.*)?", msg =>
    {
        var type = msg.Match[1];
        var option = msg.Match[2];

        switch(type)
        {
            case "projects":
                GetProjects(robot, msg, (exception, res, body) =>
                {
                    if (exception != null || res.StatusCode != HttpStatusCode.OK)
                    {
                        msg.Send("Ooops! Something went wrong");
                        return;
                    }
                    msg.Send(string.Join(Environment.NewLine, body["project"].Select(p => p["name"].Value<string>())));
                });
                break;
            case "buildTypes":
                string project = null;
                if (!string.IsNullOrEmpty(option))
                {
                    var matches = Regex.Matches(option, @"^\s*of (.*)", RegexOptions.IgnoreCase);
                    if (matches.Count > 0 && matches[0].Groups.Count > 1)
                    {
                        project = matches[0].Groups[1].Value;
                    }
                }
                GetBuildTypes(robot, msg, project, (err, res, body) => msg.Send(
                    string.Join(
                        Environment.NewLine, 
                        body["buildType"].Select(bt => string.Format("{0} of {1}", bt["name"].Value<string>(), bt["projectName"].Value<string>())))));
                        

                break;
            case "builds":
                string configuration = option;
                project = null;
                int amount = 0;
                var buildTypeMatches = Regex.Matches(option, @"^\s*of (.*?) of (.+) (\d+)",
                    RegexOptions.IgnoreCase);

                if (buildTypeMatches.Count > 0)
                {
                    configuration = buildTypeMatches[0].Groups[1].Value;
                    project = buildTypeMatches[0].Groups[2].Value;
                    amount = int.Parse(buildTypeMatches[0].Groups[3].Value);
                }
                else
                {
                    buildTypeMatches = Regex.Matches(option, @"^\s*of (.+) (\d+)",
                    RegexOptions.IgnoreCase);
                    if (buildTypeMatches.Count > 0)
                    {
                        configuration = buildTypeMatches[0].Groups[1].Value;
                        project = null;
                        amount = int.Parse(buildTypeMatches[0].Groups[2].Value);
                    }
                    else
                    {
                        amount = 5;
                        buildTypeMatches = Regex.Matches(option, @"^\s*of (.*?) of (.*)", RegexOptions.IgnoreCase);
                        if (buildTypeMatches.Count > 0)
                        {
                            configuration = buildTypeMatches[0].Groups[1].Value;
                            project = buildTypeMatches[0].Groups[2].Value;
                        }
                        else
                        {
                            buildTypeMatches = Regex.Matches(option, @"^\s*of (.*)", RegexOptions.IgnoreCase);
                            if (buildTypeMatches.Count > 0)
                            {
                                configuration = buildTypeMatches[0].Groups[1].Value;
                                project = null;
                            }
                        }
                    }
                }

                GetBuilds(robot, msg, project, configuration, amount, (err, res, body) =>
                {
                    if (body == null)
                    {
                        msg.Send(string.Format("Could not find builds for {0}", option));
                        return;
                    }
                    CreateAndPublishBuildMap(body["build"], msg);
                });

                break;
        }

            
    });

}
private void GetBuildType(Robot robot, IResponse<TextMessage> msg, string type,
    Action<Exception, HttpResponseMessage, JToken> callback)
{
    var url = string.Format("{0}/httpAuth/app/rest/buildTypes/{1}", _baseUrl, type);

    robot.Logger.Debug("sending request to #{url}");

    InvokeApiCallWithCallback(msg, callback, url);
}


private void GetCurrentBuilds(Robot robot, IResponse<TextMessage> msg, string type,
    Action<Exception, HttpResponseMessage, JToken> callback)
{
    var url = string.IsNullOrEmpty(type)
        ? string.Format("{0}/httpAuth/app/rest/builds/?locator=running:true", _baseUrl)
        : string.Format("{0}/httpAuth/app/rest/builds/?locator=buildType:{1},running:true", _baseUrl, type);

    InvokeApiCallWithCallback(msg, callback, url);
}

private void GetBuildTypes(Robot robot, IResponse<TextMessage> msg, string project,
    Action<Exception, HttpResponseMessage, JToken> callback)
{
    var projectSegment = string.IsNullOrWhiteSpace(project) ? string.Empty : string.Format("/projects/name:{0}", WebUtility.UrlEncode(project));
    var url = string.Format("{0}/httpAuth/app/rest{1}/buildTypes", _baseUrl, projectSegment);
    InvokeApiCallWithCallback(msg, (exception, message, res) =>
    {
        if (exception != null && message.StatusCode == HttpStatusCode.OK)
        {
            _buildTypes = res["buildType"].ToArray();
        }
                
        callback(exception, message, res);
    }, url);
}

private void GetBuilds(Robot robot, IResponse<TextMessage> msg, string project, string configuration, int amount,
    Action<Exception, HttpResponseMessage, JToken> callback)
{
    var projectSegment = string.IsNullOrWhiteSpace(project) ? string.Empty : string.Format("/projects/name:{0}", WebUtility.UrlEncode(project));
    var url = string.Format("{0}/httpAuth/app/rest{1}/buildTypes/name:{2}/builds", _baseUrl, projectSegment, WebUtility.UrlEncode(configuration));
    InvokeApiCallWithCallback(msg, callback, url, new{ locator= string.Format("count:{0},running:any", amount)});
}

private void GetProjects(Robot robot, IResponse<TextMessage> msg, Action<Exception, HttpResponseMessage, JToken> callback)
{
    var url = string.Format("{0}/httpAuth/app/rest/projects", _baseUrl);
    InvokeApiCallWithCallback(msg, callback, url);
}

private void MapNameToIdForBuildType(Robot robot, IResponse<TextMessage> msg, string name, string project,
    Action<IResponse<TextMessage>, JToken> callback)
{
    Func<JToken, bool> filter = b => string.Equals((string)b["name"], name, StringComparison.InvariantCultureIgnoreCase) && (string.IsNullOrEmpty(project) || string.Equals((string)b["projectName"], project, StringComparison.InvariantCultureIgnoreCase));
    var result = _buildTypes.FirstOrDefault(filter);

    if (result != null)
    {
        callback(msg, result);
        return;
    }

    GetBuildTypes(robot, msg, project, (exception, message, res) => callback(msg, res["buildType"].FirstOrDefault(filter)));
}

private void MapAndKillBuilds(Robot robot, IResponse<TextMessage> msg, string name, string id, string project)
{
    var comment = string.Format("killed by {0}", robot.Alias);
    GetCurrentBuilds(robot, msg, null, (exception, res, body) =>
    {
        if (body["count"].Value<int>() == 0)
        {
            msg.Send("No builds are currently running");
            return;
        }
        MapNameToIdForBuildType(robot, msg, name, project, (response, buildType) =>
        {
            foreach (var build in body["build"])
            {
                int parsedId;
                if (name == "all" ||
                    (!string.IsNullOrWhiteSpace(id) && int.TryParse(id, out parsedId) &&
                        build["id"].Value<int>() == parsedId) ||
                    (buildType != null && string.IsNullOrWhiteSpace(id) &&
                        build["buildTypeId"].Value<string>() == buildType.Value<string>()))
                {
                    msg.Http(string.Format("{0}/ajax.html?comment={1}&submit=Stop&buildId={2}&kill", _baseUrl,
                        comment, build["id"]))
                        .Get((err, res1) =>
                        {
                            if (err != null || res1.StatusCode != HttpStatusCode.OK)
                            {
                                msg.Send("Fail! Something went wrong. Cou;dn't stop the build for some reason");
                            }
                            else
                            {
                                msg.Send("The requested builds have been killed");
                            }
                        });
                }
            }
        });
    });
}

private void MapBuildToNameList(IResponse<TextMessage> msg, JToken build)
{
    var id = (string)build["buildTypeId"];
    var url = string.Format("{0}/httpAuth/app/rest/buildTypes/id:{1}", _baseUrl, id);
    msg.Http(url)
        .Headers(GetHeaders())
        .GetJson((err, res, body) =>
        {
            if (err != null || res.StatusCode != HttpStatusCode.OK)
            {
                return;
            }
            var buildName = body["name"].ToString();
            var baseMessage = string.Format("#{0} of {1} {2}", build["number"].ToString(), buildName,
                build["webUrl"].ToString());
            string message;
            if (build["running"] != null && build["running"].Value<bool>())
            {
                message = string.Format("{0} {1}% Complete :: {2}",
                    build["status"].Value<string>() == "SUCCESS" ? "***Winning***" : "__FAILING__",
                    build["percentageComplete"].Value<int>(), baseMessage);
            }
            else
            {
                message = string.Format("{0} :: {1}",
                    build["status"].Value<string>() == "SUCCESS" ? "OK!" : "__FAILED__", baseMessage);
            }
            msg.Send(message);
        });
}

private void CreateAndPublishBuildMap(IEnumerable<JToken> builds, IResponse<TextMessage> msg)
{
    foreach (var build in builds)
    {
        MapBuildToNameList(msg, build);
    }
}


private JToken[] _buildTypes = new JToken[0];
private Regex _buildTypeRegex = new Regex("(.*?) of (.*)");

private void InvokeApiCallWithCallback(IResponse<TextMessage> msg, Action<Exception, HttpResponseMessage, JToken> callback, string url, object query = null)
{
    msg.Http(url).Headers(GetHeaders())
        .Query(query)
        .GetJson((err, res, body) =>
        {
            if (err == null && res.StatusCode != HttpStatusCode.OK)
            {
                err = new Exception(res.Content.ReadAsStringAsync().Result);
            }
            callback(err, res, body);
        });
}

