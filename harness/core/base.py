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
import os
import json
import glob
from threading import Thread
from harness.core import framework
from harness.core import threads
from collections import OrderedDict


class Harness(framework.Framework):

	def __init__(self):

		framework.Framework.__init__(self)
		self.intro = "    __  __\n"                                   
		self.intro += "   / / / /___ __________  ___  __________\n"
		self.intro += "  / /_/ / __ `/ ___/ __ \/ _ \/ ___/ ___/\n"
		self.intro += " / __  / /_/ / /  / / / /  __(__  |__  )\n"
		self.intro += "/_/ /_/\__,_/_/  /_/ /_/\___/____/____/\n"
		self.intro += "\n\tVersion 1.0"
		self.intro += '\n\n\tProject Harness \n\tAuthor:\tRich Kelley\n\tContact: rk5devmail[A T]gmail, @rgkelley5\n'
		self.intro += '\nType help or ? to list commands. \n'
		self.prompt = 'H> '

		self.load_modules()


	def load_modules(self):

		self.print_debug("Loading modules....")
		_module_names = []
		for root, dirs, files in os.walk(self.main_mod_dir):

			
			for _dir in dirs:
				if not _dir.startswith("__"):
					sys.path.append(os.path.realpath(os.path.join(root, _dir)))

			for _file in files:
				if _file.endswith(".py") and not _file.startswith("__"):
					_module_names.append(_file.split(".")[0])
			
		_modules = list(map(__import__, _module_names))

		for mod in _modules:
		    mod_path = mod.__file__.split("modules")
		    if len(mod_path) > 1:
		    	_mod_name = mod_path[1][1:].split(".")[0]	# Get the module name --> path/module
		    	self.modules[_mod_name] = mod
		    	self.add_completion('load', _mod_name)

		self.modules = OrderedDict(sorted(self.modules.items(), key=lambda t:t[0]))


	def do_load(self, args=None):

		# args = module name for now
		name = args

		if name in self.modules:

			mod = self.modules[name].Module()
			result, _globals = mod.go(self.framework_globals)
			self.framework_globals = _globals

			if type(result) is threads.ModuleThread:
				self.print_debug("Starting background job...")
				self._add_job(name, result)
				result.start()

		else:
			self.print_error("Unknown module specified")


	def show_options(self, args=None):

		
		super().show_globals()
		print()
		super().show_options()

	

