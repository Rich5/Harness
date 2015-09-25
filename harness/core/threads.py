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
from collections import namedtuple

class ThreadException(Exception):
	pass

class ModuleThread(threading.Thread):


	def __init__(self, target, args=None):

		super().__init__()
		self.stopper = args[0]
		self.allow_print = args[1]
		self.mod_id = args[2]
		self.stdin_q = args[3]
		self.target = target.run_module
		self.post_target = target.post_run
		self.isstopped = False

	def stop(self):

		self.stopper.set()

	def stopped(self):

		return self.isstopped

	def enable_print(self):

		self.allow_print.set()
		

	def disable_print(self):

		self.allow_print.clear()
		

	def run(self):

		self.isstopped = self.target()
		self.post_target()

		