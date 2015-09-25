'''

	Harness Toolset

	Copyright (c) 2015 Rich Kelley

	Contact: 
	    @RGKelley5
	    RK5DEVMAIL[A T]gmail[D O T]com
	    www.frogstarworldc.com

	License: MIT


'''

import threading
import builtins
import sys
from random import randint
from harness.core import framework
from harness.core import threads
from collections import namedtuple
from queue import Queue



class ModuleFrame(framework.Framework):


	def __init__(self, about):

		# -----------------------------------------------------
		# Thread Events must be initialized before framework 
		# due to print function thread controls in ModuleFrame
		# -----------------------------------------------------

		self.stopper = threading.Event()
		self.stopper.clear()

		self.allow_print = threading.Event()
		self.allow_print.isSet()

		self.stdin_q = Queue()
		self.FORCE_THREAD = False

		# -----------------------------------------------------

		framework.Framework.__init__(self)

		self.prompt = "H_MOD(" + about["name"] + ") "
		self.thread_to_return = None
		self.module_id = randint(1, 100000)

		# TODO: add exception handling for undeclared keys
		self.name = about['name']
		self.author = about['author']
		self.info = about['info']
		self.contact = about['contact']
		self.version = about['version']

	def isrunning(self):

		if self.stopper.isSet():
			return False

		return True

	def print(self, *objects, sep=' ', end='\n', file=sys.stdout, flush=False):

		if self.allow_print.isSet():

			return builtins.print(*objects, sep=sep, end=end, file=file, flush=flush)

	def print_error(self, outstr):

		if self.allow_print.isSet():
			
			framework.Framework.print_error(self, outstr)

	def print_output(self, outstr):

		if self.allow_print.isSet():
			
			framework.Framework.print_output(self, outstr)

	def print_debug(self, outstr):

		if self.allow_print.isSet():
    		
			framework.Framework.print_debug(self, outstr)

	def add_session(self, remote_conn_info=None, local_conn_info=None, stype=None):
		
		return framework.Framework.add_session(self, remote_conn_info=remote_conn_info, local_conn_info=local_conn_info, id=self.module_id, stype=stype)

	def go(self, _globals):

		self.framework_globals = _globals
		self.cmdloop()
		
		return self.thread_to_return, self.framework_globals	# Return thread back to base for management

	def do_back(self, args=None):

		return True

	def do_run(self, args=None):

		if args:
			_args = framework.parse_args(args)
		else:
			_args = (" ")

		if not self.options.required_set():
			self.allow_print.set()
			self.print_error("Required options not set")
			self.print_error("Check 'Required' column\n")
			self.show_options()
			self.allow_print.clear()
			return

		self.stopper.clear()
		self.allow_print.set()

		# Wrap the module in a Thread object and return to base
		if self.FORCE_THREAD or _args[0].lower() in ('job', 'thread', 'j', 't'):

			if self.FORCE_THREAD:
				self.print_output("Module must be run in background!")

			self.allow_print.clear()

			t = threads.ModuleThread(target=self, args=[self.stopper, self.allow_print, self.module_id, self.stdin_q])
			t.daemon = True
			self.thread_to_return = t
			return True

		else:

			# Normal run in foreground
			try:

				self.run_module()

			# Exit the module cleanly without exiting framework
			except KeyboardInterrupt:
				pass

			finally:
				self.cleanup_exit()

	def show_info(self, args=None):

		print("\n\tModule Name: ", self.name)
		print("\tAuthors: ", self.author)
		print("\tContact: ", self.contact)
		print("\tInfo: ", self.info)
		print("\tVersion: ", self.version)

		print()

	def pre_run(self, args=None):
		pass

	def run_module(self, args=None):
		pass

	def post_run(self, args=None):
		pass

	def cleanup_exit(self):

		self.print_debug("Cleaning up...")
		
		self.stopper.clear()
		self.post_run()
		self.allow_print.clear()

		self.print_output("Exiting module...")
		return True



	

