'''

    Harness Toolset

    Copyright (c) 2015 Rich Kelley

    Contact: 
        @RGKelley5
        RK5DEVMAIL[A T]gmail[D O T]com
        www.frogstarworldc.com

    License: MIT


'''


import sys
import cmd
import os
import json
import glob
import configparser
import asyncio
from harness.core import global_vars
from threading import Lock
from queue import Queue
from collections import namedtuple
from collections import OrderedDict


'''

    Option class as a simple data container

'''
class Option:

    def __init__(self, name, value, otype, req):

        self.name = value
        self.type = otype
        self.req = req

'''

    Options class to enforce option data types

'''
class Options:

    def __init__(self):
        
        super().__setattr__("opts", OrderedDict())        

    def add(self, name, value, otype, req=False):

        if otype.lower() not in ('str', 'int', 'list', 'bool', 'float'):

            print("[!] Bad option type")  #need to raise framework error when I get around to it
            return

        self.opts[name] = Option(name, value, otype, req)

        if not value == "" and not value == None:
            self.__setattr__(name, value)

    def remove(self, name):
        
        del(self.opts[name])

    def __setattr__(self, name, value):


        if name in self.opts:
            
            _type = self.opts[name].type.lower()

            if _type == 'int':
                self.opts[name].name = int(value)

            elif _type == 'bool':
                if value.lower() in ('false', 0):
                    self.opts[name].name = False

                elif value.lower() in ('true', 1):
                    self.opts[name].name = True

            elif _type == 'list':

                self.opts[name].name = [i.strip() for i in list(value.split(","))]

            elif _type == 'str':

                self.opts[name].name = str(value)

            elif _type == 'float':

                self.opts[name].name = float(value)

        else:

            super().__setattr__(name, value)

    def __getattr__(self, key):


        if key in self.opts:
            return self.opts[key].name

        else:

            super().__getattr__(key)


    def __iter__(self):

        self.options = iter(self.opts.items())
        return self

    def __next__(self):

        return next(self.options)

    def __contains__(self, item):

        if item in self.opts:
            return True

        return False

    def __bool__(self):

        if self.opts:
            return True

        return False

    def required_set(self):

        for key in self.opts:
            if self.opts[key].req:
                if self.opts[key].name == "" or self.opts[key].name == None:
                    return False

        return True

'''

    Session class as a simple data container

'''
class Session:

    def __init__(self, SID, remote_conn_info, local_conn_info, id):

        self.SID = SID
        self.mod_id = id
        self.remote_conn_info = remote_conn_info
        self.local_conn_info = local_conn_info
        self.session_in = asyncio.Queue()
        self.session_out = asyncio.Queue()
        self.history = []
   

class Framework(cmd.Cmd):


    def __init__(self):

        cmd.Cmd.__init__(self)
        setup = configparser.ConfigParser()
        setup.read("harness/core/config.ini")
        self.main_mod_dir = setup.get('Main', 'main_mod_dir')

        self.completions = {'set': [],
                            'setg': [],
                            'show': [],
                            'load': [],
                            'session': global_vars.session_completions,
                            'kill': [],
                            'save': [],
                            'resource': []
                            }

        self.framework_globals = Options()
        self.add_global("DEBUG", "False", "bool")
    
        self.options = Options()
        self.modules = OrderedDict()
        self.jobs = {}
        
        for name in self.completions:
            self._load_completions(name)

        '''

            Make good choices, and use the framework "session functions" rather than trying to edit the global session variables directly.

        '''
        self.sessions = global_vars.sessions
        self.sess_lock = global_vars.session_lock

    """
    
        Begin Cmd line parsing functions
    
    """
        

    def do_set(self, arg=""):

        self._set_cmd(*parse_args(arg))

    def do_setg(self, arg=""):

        self._setg_cmd(*parse_args(arg))

    def do_session(self, arg=""):

        self._session_cmd(*parse_args(arg))

    def do_show(self, arg=""):
    
        self._show_cmd(*parse_args(arg))
        
    def do_save(self, arg=""):
    
        self._save_cmd(*parse_args(arg))

    def do_exit(self, arg=None):
    
        self.exit()
        
    def do_quit(self, arg=None):
    
        self.exit()
        
    def do_resource(self, arg=""):
    
        self._resource_cmd(*parse_args(arg))

    def do_kill(self, args=""):

        self._kill_cmd(*parse_args(args))
    
    def do_load(self, arg=""):
    
        self._load_cmd(*parse_args(arg))
        
    def do_shell(self, arg=""):
    
        self.do_sh(arg)
        
    def do_sh(self, arg=""):
    
        try:
        
            os.system(arg)
    
        except KeyboardInterrupt:
        
            print("\n")
            
    def emptyline(self):
    
        if self.lastcmd:
            self.lastcmd = ""
            return self.onecmd('\n')
        
    '''

        Completion functions

    '''
    def add_completion(self, main_cmd, sub_cmd):

        if main_cmd.lower() in self.completions:

            self.completions[main_cmd].append(sub_cmd)
            self.print_debug(str(sub_cmd) + " added to " + str(main_cmd) + " completions")

    def remove_completion(self, main_cmd, sub_cmd):

        if main_cmd.lower() in self.completions:
            self.completions[main_cmd].remove(sub_cmd)
            self.print_debug(str(sub_cmd) + " removed from " + str(main_cmd) + " completions")

    def completedefault(self, text, line, begidx, endidx):

        main_cmd = line.partition(' ')[0].strip()
        mline = line.partition(' ')[2]
        offs = len(mline) - len(text)

        if main_cmd in self.completions:
            
            return [s[offs:] for s in self.completions[main_cmd] if s.startswith(mline)]

        return []
        
    
        
    """
    
       Begin Cmd line functions
    
    """

    def show_options(self, args=None):

        if self.options:

            print("Module:")
            _opts = []
            _opt_name_display = ""
            _opt_req_display = ""

            for name, option in self.options:

                if option.type == "bool":
                    if option.name:
                        _opt_name_display = "True"
                    else:
                        _opt_name_display = "False"
                else:
                    _opt_name_display = option.name


                if option.req:
                    _opt_req_display = "True"
                else:
                    _opt_req_display = "False"


                _opts.append([name, _opt_name_display, option.type, _opt_req_display])
            
            print_table(_opts, ("Option", "Value", "Type", "Required"), clean_print=True)


    def show_globals(self, args=None):

        if self.framework_globals:
            print("Globals:")
            _opts = []
            _opt_name_display = ""
            for name, option in self.framework_globals:

                if option.type == "bool":
                    if option.name:
                        _opt_name_display = "True"
                    else:
                        _opt_name_display = "False"
                else:
                    _opt_name_display = option.name

                _opts.append([name, _opt_name_display, option.type])
                
            print_table(_opts, ("Option", "Value", "Type"), clean_print=True)


    def add_option(self, name, value, otype, req=False):

        self.options.add(name, value, otype, req)
        self.add_completion('set', name)

    def add_global(self, name, value, otype):

        self.framework_globals.add(name, value, otype)
        self.add_completion('setg', name)


    def _set_cmd(self, *args):
        
        if len(args) != 2:
            return  # raise framework exception. we need varname, value

        if args[0] in self.options:

            setattr(self.options, args[0], args[1])

        else:
            self.print_error("Unknown variable")
            return

    def _setg_cmd(self, *args):
        
        if len(args) != 2:
            return  # raise framework exception. we need varname, value

        if args[0] in self.framework_globals:

            setattr(self.framework_globals, args[0], args[1])

        else:
            self.print_error("Unknown global variable")
            return

    def _load_completions(self, main_cmd=None):
        
        if not main_cmd:
            return

        for _fns in self.get_names():
            if _fns.startswith(main_cmd):
                _, opt = _fns.split("_")
                self.add_completion(main_cmd, opt)


    def _show_cmd(self, target=None, *args):

        if not target:
            self.help_show()
            return
    
        _fns = "show_" + target
        if _fns in self.get_names():
        
            getattr(self, _fns)(args)

        else:
            self.print_error("Unknown option")
            
    
    def _save_cmd(self, target=None, *args):
    
        _fns = "save_" + target
        if _fns in self.get_names():
        
            getattr(self, _fns)(args)

        else:
            self.print_error("Unknown option")
    
    def _resource_cmd(self, filename=None, *args):
        
        try:
        
            with open(filename, 'r') as resourcefile:
            
                for cmd in resourcefile:
            
                    self.onecmd(cmd)
            
        except IOError:
        
            self.print_error("Resource File Not Found")
            
            
            
    def show_modules(self, args=None):
        
        if self.modules:
            categories = []
            print()
            for name, mod in self.modules.items():
                _cat = name.split(os.path.sep)[0]
                if not _cat in categories:
                    categories.append(_cat)

            
            for cat in categories:
                print("\t" + cat.capitalize())
                print('\t'+ '-'*len(cat))
                for name, mod in self.modules.items():
                    if name.split(os.path.sep)[0] == cat:
                        print('\t' + name)

                print()

    
    
    def exit(self):
    
        self.cleanup()
        
        print("Exiting...")
        sys.exit(0)
        
    # todo
    def _create_config(self):

        pass

    def cleanup(self):
            
        return


    """

        Begin session management functions

    """
    def add_session(self, remote_conn_info=None, local_conn_info=None, id=None, stype=None):

        if not remote_conn_info or not local_conn_info or not id:
            self.print_debug("Connection information not specified")
            return

        SID = self.gen_session_id()

        with self.sess_lock:
            self.sessions[SID] = Session(SID, remote_conn_info, local_conn_info, id=id)

        self.add_completion("session", str(SID))
        print("")
        self.print_alert("Session " + str(SID) + " added: " + str(remote_conn_info) + " <--> " + str(local_conn_info))

        return SID

    def remove_session(self, SID=None):

        if not SID:
            return

        try:
            SID = int(SID)
        except:
            self.print_debug("Invalid SID")
            return

        if SID in self.sessions:
            with self.sess_lock:
                del(self.sessions[SID])
                self.remove_completion("session", str(SID))

            print("")
            self.print_alert("Session " + str(SID) + " removed")

    def gen_session_id(self):

        if self.sessions:
            
            SID = max([SID for SID in self.sessions]) + 1
            self.print_debug("generating SID: " + str(SID))
            return SID

        else:
            return 1

    def _session_cmd(self, SID=None):

        if not SID:
            return

        try:
            SID = int(SID)
        except:
            self.print_debug("Invalid SID")
            return

        if not SID in self.sessions:
            return

        mod_id = self.sessions[SID].mod_id

        # Find the stdin_q assigned to Module
        job = None
        for name, _job in self.jobs.items():
            if _job[1].mod_id == mod_id:
                job = _job[1]
                stdin_q = job.stdin_q
                break
        else:
            self.print_debug("Cannot match session to module")
            return

        job.enable_print()

        stdin_q.put((SID, "\r\n"))  # Trigger the first callback prompt

        while True:

            _cmd = sys.stdin.readline()
            if _cmd.lower() == "background\n" or _cmd.lower() == "bg\n":
                self.print_debug("sending session to background")
                job.disable_print()
                break;

            if _cmd == "":
                _cmd = "\r\n"
          
            if SID not in self.sessions:
                break

            stdin_q.put((SID, _cmd))
            self.sessions[SID].history.append(_cmd)
        
        job.disable_print()
            
    def show_sessions(self, args=None):

        for SID, session in self.sessions.items():
            print(SID, str(session.remote_conn_info), " <--> ", str(session.local_conn_info))
            

    """

        Begin Job Functions

    """

    def _gen_job_id(self):

        if self.jobs:
            
            JID = max([JID for JID in self.jobs]) + 1
            self.print_debug("generating JID: " + str(JID))
            return JID

        else:
            self.print_debug("First JID. Setting to 1")
            return 1

    def _add_job(self, name, job):

        
        JID = self._gen_job_id()

        self.jobs[JID] = (name, job)
        self.add_completion("kill", str(JID))

        self.print_alert("Module (" + str(name) + ") started as job. ID: " + str(JID))

        return JID


    def show_jobs(self, args=None):

        for JID, job in self.jobs.items():
            print(str(JID), job[0]) # 0 = name, 1 = object


    def _kill_cmd(self, args=None):

        JID2del = []

        if not args:
            self.print_error("Job ID or 'All' required as arg")
            return

        if args.lower() in ('all'):

            self.print_alert("killing all background jobs...")
            for JID, job in self.jobs.items():

                self.print_alert("killing job (" + str(JID) + ")")
                job[1].stop()
                self.print_debug("waiting for job (" + str(JID) + ") to finish")
                job[1].join(3.0)
                self.print_debug("removing job (" + str(JID) + ") from manager")
                JID2del.append(JID)

            for JID in JID2del:
                del(self.jobs[JID])

            if self.jobs:
                self.print_debug("Unknown error. Could not remove some jobs")

        else: 
            JID = args

            try:
                JID = int(JID)  # If args != all then should be JID (int)
            except:
                self.print_debug("Int not found")
                return

            if JID in self.jobs:
                
                self.print_output("killing job (" + str(JID) + ")")
                job = self.jobs[JID][1]
                job.stop()
                self.print_debug("waiting for job (" + str(JID) + ") to finish")
                job.join(3.0)
                self.print_debug("removing job (" + str(JID) + ") from manager")
                JID2del.append(JID)

            for JID in JID2del:
                del(self.jobs[JID])


    """

        Begin Print functions

    """

    def print_alert(self, outstr):

        print("[!] ", outstr)

    def print_error(self, outstr):
        
        print("[-] ", outstr)

    def print_output(self, outstr):
        
        print("[*] ", outstr)

    def print_debug(self, outstr):

        if self.framework_globals.DEBUG:
            print("[DEBUG] ", outstr)

    """
    
        Begin Help functions for cmd line
    
    """
    def help_load(self):
    
        print("load {module name}")
                 
    def help_show(self):
    
        print("show [globals|jobs|modules|options|sessions]")
       
    def help_exit(self):
    
        print("really")
       
    def help_quit(self):
    
        print("...")
    
    def help_sh(self):
    
        print("execute shell commands on current system\n !cmd")
    
    def help_shell(self):
    
        print("execute shell commands on current system\n !cmd")
    
    def help_resource(self):

        print("Not implemented")

    def help_save(self):

        print("Not implemented")

    def help_kill(self):

        print("kill {JID}")

    def help_session(self):

        print("session {SID}")

    def help_set(self):

        print("set {SID} {value}")

    def help_setg(self):

        print("setg {SID} {value}")

def parse_args(arg):

    # convert a series of zero or more strings to an argument tuple
    return tuple(map(str, arg.split()))


# The following function was constructed in a hurry. Edit at your own risk
def print_table(data, fields=(), records_per_page=None, clean_print=None):

    lengths = dict(zip(fields, tuple(len(_) for _ in fields)))
    mapping = [dict(zip(fields, record)) for record in data]    
    
    for row in mapping:
        for item in row:
            lengths[item] = max(len(str(row[item])), lengths[item])
    
    row_format = ""
    line_format = ""
    clean_row_format = ""
    clean_line_format = ""
    for field in fields:
        row_format += "{:^" + str(lengths[field]) + "}  |  "
        line_format += "{:^" + str(lengths[field]) + "}--+--"
        clean_row_format += "{:<" + str(lengths[field]) + "}     "
        clean_line_format += "{:<" + str(lengths[field]) + "}     "
    
    headers = row_format.format(*fields)
    clean_headers = clean_row_format.format(*fields)

    headers = "|  " + headers
    clean_headers = "   " + clean_headers

    lines = ("-"*lengths[field] for field in fields)
    
    solid_line = "-"*(len(headers.strip("")))
    clean_solid_line = " "*(len(headers.strip("")))
    solid_line = "|--" + solid_line[:len(solid_line)-6] + "|"
    clean_solid_line = "   " + clean_solid_line[:len(solid_line)-6] + " "
    
    
    spacer_line = line_format.format(*lines)
    spacer_line = "|--" + spacer_line[:len(spacer_line)-3] + "|"

    lines = ("-"*lengths[field] for field in fields)
    clean_spacer_line = clean_line_format.format(*lines)
    clean_spacer_line = "   " + clean_spacer_line[:len(clean_spacer_line)-3] + " "
    

    if clean_print:

        print()
        print(clean_headers)       # e.g.  field1  
        print(clean_spacer_line)    #      ------
        
        num_rows = len(data)
        current_row = 0
        for row in data:
            current_row += 1
            data_row = clean_row_format.format(*row)
            print("   " + data_row)
            
            if records_per_page:
                if current_row % records_per_page == 0:
                    r = input("")
        print('\n')  


    else:


        print(solid_line)    # e.g. |--------|
        print(headers)       # e.g. | field1 | 
        print(spacer_line)   # e.g. |---+----|
        
        num_rows = len(data)
        current_row = 0
        for row in data:
            current_row += 1
            data_row = row_format.format(*row)
            print("|  " + data_row)

            if current_row == num_rows:
                print(solid_line)
                
            else:
                print(spacer_line)
            
            if records_per_page:
                if current_row % records_per_page == 0:
                    r = input("")
        print  
